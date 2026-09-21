// DisplayNameSimModule for OpenSimulator 0.9.3.x
// Reconstructed from a validated OpenSimulator Display Names implementation.
//
// The module is intentionally conservative: it keeps the viewer-facing
// Display Name CAPS behavior while reading the service settings from the
// standard [DisplayNameCaps] section in OpenSim.ini.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Capabilities;

// Explicit aliases avoid name collisions introduced by OpenMetaverse/OpenSim 0.9.3.x.
using Caps = OpenSim.Framework.Capabilities.Caps;
using OSDMap = OpenMetaverse.StructuredData.OSDMap;
using OSDArray = OpenMetaverse.StructuredData.OSDArray;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace DisplayNameSimModule
{
    public class DisplayNameCapsModule : ISharedRegionModule
    {
        internal static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod()!.DeclaringType);

        // ServiceBase must be supplied by [DisplayNameCaps] in OpenSim.ini.
        // No grid-specific service URL is compiled into the module.
        internal static string ServiceBase = string.Empty;
        internal static int HttpTimeoutMs = 5000;
        internal static TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
        internal static readonly TimeSpan ErrorCacheTtl = TimeSpan.FromMinutes(2);
        internal static int MaxConcurrentServiceCalls = 5;
        internal static bool Verbose = true;

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<UUID, CachedName> Cache = new Dictionary<UUID, CachedName>();
        private static readonly ConcurrentDictionary<UUID, Task<CachedName>> Inflight = new ConcurrentDictionary<UUID, Task<CachedName>>();

        private static readonly object CapsAgentsLock = new object();
        private static readonly HashSet<int> CapsAgentsRegisteredOnPort = new HashSet<int>();

        private static readonly object CapsExactLock = new object();
        private static readonly HashSet<string> CapsExactRegistered = new HashSet<string>(StringComparer.Ordinal);

        private static SemaphoreSlim HttpLimiter = new SemaphoreSlim(MaxConcurrentServiceCalls);

        public string Name => "DisplayNameCapsModule";
        public Type ReplaceableInterface => null;

        public void Initialise(IConfigSource source)
        {
            IConfig cfg = source.Configs["DisplayNameCaps"];
            if (cfg != null)
            {
                ServiceBase = (cfg.GetString("ServiceBase", ServiceBase) ?? ServiceBase).Trim().TrimEnd('/');
                HttpTimeoutMs = Math.Max(500, cfg.GetInt("HttpTimeoutMs", HttpTimeoutMs));
                CacheTtl = TimeSpan.FromMinutes(Math.Max(1, cfg.GetInt("CacheTtlMinutes", (int)CacheTtl.TotalMinutes)));
                MaxConcurrentServiceCalls = Math.Max(1, cfg.GetInt("MaxConcurrentServiceCalls", MaxConcurrentServiceCalls));
                Verbose = cfg.GetBoolean("Verbose", Verbose);
            }

            // Recreate limiter after config is known.
            HttpLimiter.Dispose();
            HttpLimiter = new SemaphoreSlim(MaxConcurrentServiceCalls, MaxConcurrentServiceCalls);

            if (string.IsNullOrWhiteSpace(ServiceBase))
            {
                Log.Error("[DisplayNameCaps]: ServiceBase is empty. Configure [DisplayNameCaps] in OpenSim.ini.");
            }
            else
            {
                Log.InfoFormat(
                    "[DisplayNameCaps]: MODULE LOADED. ServiceBase={0} TimeoutMs={1} CacheTtlMinutes={2} MaxConcurrentServiceCalls={3} Verbose={4}",
                    ServiceBase, HttpTimeoutMs, (int)CacheTtl.TotalMinutes, MaxConcurrentServiceCalls, Verbose);
            }
        }

        public void PostInitialise() { }
        public void Close() { }
        public void AddRegion(Scene scene) { }
        public void RemoveRegion(Scene scene) { }

        public void RegionLoaded(Scene scene)
        {
            try
            {
                int port = Convert.ToInt32(scene.RegionInfo.HttpPort);
                object server = GetHttpServerFromMainServerPort(port);
                if (server != null)
                    TryRegisterCapsAgentsEndpointOnce(server, scene, port);
                else
                    Log.ErrorFormat("[DisplayNameCaps]: RegionLoaded could not get HttpServer for port {0}", port);
            }
            catch (Exception e)
            {
                Log.Error("[DisplayNameCaps]: RegionLoaded HTTP registration failed", e);
            }

            scene.EventManager.OnRegisterCaps += (agentId, caps) => OnRegisterCaps(scene, agentId, caps);
        }

        private void OnRegisterCaps(Scene scene, UUID agentId, Caps caps)
        {
            try
            {
                object server = GetHttpServerFromMainServer(caps);
                if (server == null)
                {
                    Log.Error("[DisplayNameCaps]: Could not obtain HTTP server for CAPS port");
                    return;
                }

                AddCapsExactHandlers(server, scene, agentId, caps.CapsObjectPath);
                TryRegisterCapsAgentsEndpointOnce(server, scene, Convert.ToInt32(caps.Port));
                RegisterSeedCaps(scene, caps);

                Log.InfoFormat("[DisplayNameCaps]: Registered for agent={0} port={1} capsPath={2}",
                    agentId, caps.Port, caps.CapsObjectPath);
            }
            catch (Exception e)
            {
                Log.Error("[DisplayNameCaps]: Exception during OnRegisterCaps", e);
            }
        }

        private static bool AddCapsExactHandlers(object server, Scene scene, UUID agentId, string capsObjectPath)
        {
            if (string.IsNullOrWhiteSpace(capsObjectPath))
                return false;

            string key = server.GetHashCode().ToString() + ":" + capsObjectPath;
            lock (CapsExactLock)
            {
                if (CapsExactRegistered.Contains(key))
                    return true;
                CapsExactRegistered.Add(key);
            }

            try
            {
                AgentCapsRouter router = new AgentCapsRouter(scene, agentId);
                string setPath = "/CAPS/" + capsObjectPath.Trim('/') + "/set_display_name";
                string resetPath = "/CAPS/" + capsObjectPath.Trim('/') + "/reset_display_name";

                // OpenSim 0.9.3.x exposes SimpleStreamHandler and IHttpServer.
                // We still invoke AddSimpleStreamHandler by reflection to remain
                // tolerant of small method-signature changes in future releases.
                AddSimpleHandlerDynamic(server, new SimpleStreamHandler(setPath, (req, resp) => router.HandleSet(req, resp)));
                AddSimpleHandlerDynamic(server, new SimpleStreamHandler(resetPath, (req, resp) => router.HandleReset(req, resp)));
                return true;
            }
            catch (Exception e)
            {
                lock (CapsExactLock)
                    CapsExactRegistered.Remove(key);

                Log.Error("[DisplayNameCaps]: FAILED to register exact per-agent CAPS handlers", e);
                return false;
            }
        }

        private static void TryRegisterCapsAgentsEndpointOnce(object server, Scene scene, int port)
        {
            lock (CapsAgentsLock)
            {
                if (CapsAgentsRegisteredOnPort.Contains(port))
                    return;

                try
                {
                    CapsAgentsRouter router = new CapsAgentsRouter(scene);
                    AddSimpleHandlerDynamic(server,
                        new SimpleStreamHandler("/CAPS/agents", (req, resp) => router.HandleAgents(req, resp)));
                    AddSimpleHandlerDynamic(server,
                        new SimpleStreamHandler("/CAPS/agents/", (req, resp) => router.HandleAgents(req, resp)));

                    CapsAgentsRegisteredOnPort.Add(port);
                    Log.InfoFormat("[DisplayNameCaps]: Registered /CAPS/agents and /CAPS/agents/ on port {0}", port);
                }
                catch (Exception e)
                {
                    Log.Error("[DisplayNameCaps]: Failed to register /CAPS/agents", e);
                }
            }
        }

        private static void AddSimpleHandlerDynamic(object server, SimpleStreamHandler handler)
        {
            Type t = server.GetType();
            MethodInfo method = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                {
                    if (!string.Equals(m.Name, "AddSimpleStreamHandler", StringComparison.Ordinal))
                        return false;
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length >= 1 && p[0].ParameterType.IsAssignableFrom(handler.GetType());
                });

            if (method == null)
                throw new MissingMethodException(t.FullName, "AddSimpleStreamHandler");

            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[parameters.Length];
            args[0] = handler;
            for (int i = 1; i < args.Length; i++)
            {
                if (parameters[i].HasDefaultValue)
                    args[i] = parameters[i].DefaultValue;
                else if (parameters[i].ParameterType == typeof(bool))
                    args[i] = false;
                else
                    args[i] = parameters[i].ParameterType.IsValueType
                        ? Activator.CreateInstance(parameters[i].ParameterType)
                        : null;
            }
            method.Invoke(server, args);
        }

        private static void RegisterSeedCaps(Scene scene, Caps caps)
        {
            string host = GetBestExternalHost(scene);
            int port = Convert.ToInt32(caps.Port);
            string baseHttp = "http://" + host + ":" + port;
            string agents = baseHttp + "/CAPS/agents";
            string capPath = caps.CapsObjectPath.Trim('/');
            string set = baseHttp + "/CAPS/" + capPath + "/set_display_name";
            string reset = baseHttp + "/CAPS/" + capPath + "/reset_display_name";

            TryRegisterCapUrl(caps, "AvatarNameCache", agents);
            TryRegisterCapUrl(caps, "GetDisplayNames", agents);
            TryRegisterCapUrl(caps, "DisplayNames", agents);
            TryRegisterCapUrl(caps, "SetDisplayName", set);
            TryRegisterCapUrl(caps, "ResetDisplayName", reset);

            if (Verbose)
            {
                Log.InfoFormat("[DisplayNameCaps]:[SEED] AvatarNameCache={0}", agents);
                Log.InfoFormat("[DisplayNameCaps]:[SEED] SetDisplayName={0}", set);
                Log.InfoFormat("[DisplayNameCaps]:[SEED] ResetDisplayName={0}", reset);
            }
        }

        private static string GetBestExternalHost(Scene scene)
        {
            try
            {
                string host = scene.RegionInfo.ExternalHostName;
                if (!string.IsNullOrWhiteSpace(host))
                    return host;
            }
            catch { }

            try
            {
                string uri = scene.RegionInfo.ServerURI;
                if (Uri.TryCreate(uri, UriKind.Absolute, out Uri u) && !string.IsNullOrWhiteSpace(u.Host))
                    return u.Host;
            }
            catch { }

            return "127.0.0.1";
        }

        private static bool TryRegisterCapUrl(Caps caps, string capabilityName, string url)
        {
            Type type = caps.GetType();
            foreach (string methodName in new[] { "RegisterHandler", "SetCap" })
            {
                try
                {
                    MethodInfo mi = type.GetMethod(methodName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(string), typeof(string) }, null);
                    if (mi != null)
                    {
                        mi.Invoke(caps, new object[] { capabilityName, url });
                        return true;
                    }
                }
                catch { }
            }

            // Compatibility fallback used by the recovered v4: locate the seed
            // caps map/property and set the capability URL through its indexer.
            foreach (string propName in new[] { "SeedCaps", "Caps", "m_caps", "m_seedCaps" })
            {
                try
                {
                    object map = null;
                    PropertyInfo p = type.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null)
                        map = p.GetValue(caps, null);
                    else
                    {
                        FieldInfo f = type.GetField(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (f != null)
                            map = f.GetValue(caps);
                    }

                    if (map is OSDMap osdMap)
                    {
                        osdMap[capabilityName] = OSD.FromString(url);
                        return true;
                    }

                    if (map != null)
                    {
                        PropertyInfo indexer = map.GetType().GetProperty("Item", new[] { typeof(string) });
                        if (indexer != null && indexer.CanWrite)
                        {
                            object value = indexer.PropertyType == typeof(OSD) ? OSD.FromString(url) : (object)url;
                            indexer.SetValue(map, value, new object[] { capabilityName });
                            return true;
                        }
                    }
                }
                catch { }
            }

            Log.ErrorFormat("[DisplayNameCaps]: Unable to expose capability {0}", capabilityName);
            return false;
        }

        internal static void SplitLegacy(string legacy, out string first, out string last)
        {
            first = "Resident";
            last = string.Empty;
            if (string.IsNullOrWhiteSpace(legacy))
                return;

            string[] parts = legacy.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            first = parts.Length > 0 ? parts[0] : "Resident";
            last = parts.Length > 1 ? parts[1] : "Resident";
        }

        internal static string UsernameFromLegacy(string legacy)
        {
            SplitLegacy(legacy, out string first, out string last);
            return (first + "." + last).Trim('.').ToLowerInvariant();
        }

        internal static CachedName GetCachedOrFetch(UUID id)
        {
            lock (CacheLock)
            {
                if (Cache.TryGetValue(id, out CachedName cached) && cached.ExpiresUtc > DateTime.UtcNow)
                    return cached;
            }

            Task<CachedName> task = Inflight.GetOrAdd(id, key => FetchAndCacheAsync(key));
            try
            {
                return task.GetAwaiter().GetResult();
            }
            finally
            {
                if (task.IsCompleted)
                    Inflight.TryRemove(id, out _);
            }
        }

        private static async Task<CachedName> FetchAndCacheAsync(UUID id)
        {
            if (string.IsNullOrWhiteSpace(ServiceBase))
                return CacheEmpty(id, ErrorCacheTtl);

            await HttpLimiter.WaitAsync().ConfigureAwait(false);
            try
            {
                string url = ServiceBase.TrimEnd('/') + "/get?id=" + Uri.EscapeDataString(id.ToString());
                string json = await Task.Run(() => HttpGet(url)).ConfigureAwait(false);

                // A 404 from the companion service means that this UUID is not
                // a local account handled by the Display Name service.  This is
                // common for Hypergrid users.  Keep that state distinct from a
                // transient service failure so /CAPS/agents can place the UUID
                // in bad_ids and let the viewer fall back to OpenSim's legacy/HG
                // name resolution instead of manufacturing "Unknown Resident".
                string serviceStatus = JsonGetString(json, "status");
                if (string.Equals(serviceStatus, "not_found", StringComparison.OrdinalIgnoreCase))
                    return CacheNotFound(id, ErrorCacheTtl);

                if (!JsonOk(json))
                    return CacheEmpty(id, ErrorCacheTtl);

                string displayName = JsonGetString(json, "display_name") ?? string.Empty;
                string next = JsonGetString(json, "next_change_allowed") ?? string.Empty;

                CachedName result = new CachedName
                {
                    DisplayName = displayName,
                    NextAllowedUtc = ParseIsoUtcOrNow(next),
                    ExpiresUtc = DateTime.UtcNow.Add(CacheTtl)
                };

                lock (CacheLock)
                    Cache[id] = result;

                return result;
            }
            catch (Exception e)
            {
                Log.ErrorFormat("[DisplayNameCaps]:[SERVICE][GET] failed id={0} err={1}", id, e.Message);
                return CacheEmpty(id, ErrorCacheTtl);
            }
            finally
            {
                HttpLimiter.Release();
            }
        }

        private static CachedName CacheEmpty(UUID id, TimeSpan ttl)
        {
            CachedName empty = new CachedName
            {
                DisplayName = string.Empty,
                NextAllowedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.Add(ttl),
                ServiceNotFound = false
            };
            lock (CacheLock)
                Cache[id] = empty;
            return empty;
        }

        private static CachedName CacheNotFound(UUID id, TimeSpan ttl)
        {
            CachedName missing = new CachedName
            {
                DisplayName = string.Empty,
                NextAllowedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.Add(ttl),
                ServiceNotFound = true
            };
            lock (CacheLock)
                Cache[id] = missing;
            return missing;
        }

        internal static void ClearCache(UUID id)
        {
            lock (CacheLock)
                Cache.Remove(id);
        }

        internal static bool ServiceSet(UUID id, string name)
        {
            if (string.IsNullOrWhiteSpace(ServiceBase))
                return false;

            string url = ServiceBase.TrimEnd('/') + "/set";
            string body = "id=" + Uri.EscapeDataString(id.ToString()) +
                          "&name=" + Uri.EscapeDataString(name ?? string.Empty);
            try
            {
                string json = HttpPostForm(url, body);
                if (Verbose)
                    Log.InfoFormat("[DisplayNameCaps]:[SERVICE][SET] {0} -> {1}", url, TrimForLog(json, 160));
                return JsonOk(json);
            }
            catch (Exception e)
            {
                Log.ErrorFormat("[DisplayNameCaps]:[SERVICE][SET] failed url={0} err={1}", url, e.Message);
                return false;
            }
        }

        internal static bool ServiceReset(UUID id)
        {
            if (string.IsNullOrWhiteSpace(ServiceBase))
                return false;

            string url = ServiceBase.TrimEnd('/') + "/reset";
            string body = "id=" + Uri.EscapeDataString(id.ToString());
            try
            {
                string json = HttpPostForm(url, body);
                if (Verbose)
                    Log.InfoFormat("[DisplayNameCaps]:[SERVICE][RESET] {0} -> {1}", url, TrimForLog(json, 160));
                return JsonOk(json);
            }
            catch (Exception e)
            {
                Log.ErrorFormat("[DisplayNameCaps]:[SERVICE][RESET] failed url={0} err={1}", url, e.Message);
                return false;
            }
        }

        private static string HttpGet(string url)
        {
            using HttpClient client = CreateHttpClient();
            using HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();

            // The display-name backend legitimately returns 404 for UUIDs that
            // are not local accounts (for example HG users or stale viewer
            // name-cache entries).  This is not a module failure: treat it as
            // "no custom display name" and let OpenSim's legacy name remain.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return "{\"status\":\"not_found\"}";

            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        private static string HttpPostForm(string url, string body)
        {
            using HttpClient client = CreateHttpClient();
            using StringContent content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
            using HttpResponseMessage response = client.PostAsync(url, content).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        private static HttpClient CreateHttpClient()
        {
            return new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(HttpTimeoutMs)
            };
        }

        private static string JsonGetString(string json, string property)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty(property, out JsonElement el))
                    return null;
                return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool JsonOk(string json)
        {
            string value = JsonGetString(json, "ok");
            if (!string.IsNullOrEmpty(value))
                return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";

            // The companion web backend returns status="ok".
            string status = JsonGetString(json, "status");
            return string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase);
        }

        internal static DateTime ParseIsoUtcOrNow(string value)
        {
            // The backend returns ISO-8601 values such as
            // 2026-09-18T12:59:59Z.  RoundtripKind cannot legally be combined
            // with AdjustToUniversal, so parse as DateTimeOffset and normalize
            // to UTC instead.
            if (DateTimeOffset.TryParse(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                    out DateTimeOffset dto))
                return dto.UtcDateTime;

            return DateTime.UtcNow;
        }

        private static string TrimForLog(string value, int max)
        {
            if (value == null) return string.Empty;
            return value.Length <= max ? value : value.Substring(0, max);
        }

        internal static void WriteLlsd(IOSHttpResponse response, OSD value, int statusCode = 200)
        {
            string xml = OSDParser.SerializeLLSDXmlString(value, false);
            response.StatusCode = statusCode;
            response.ContentType = "application/llsd+xml";
            response.RawBuffer = Encoding.UTF8.GetBytes(xml);
        }

        private static object GetHttpServerFromMainServer(Caps caps)
        {
            return GetHttpServerFromMainServerPort(Convert.ToInt32(caps.Port));
        }

        private static object GetHttpServerFromMainServerPort(int port)
        {
            try
            {
                Type mainServerType = typeof(MainServer);
                PropertyInfo instanceProp = mainServerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                object instance = instanceProp?.GetValue(null, null);
                if (instance == null)
                    return null;

                foreach (MethodInfo mi in mainServerType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (!string.Equals(mi.Name, "GetHttpServer", StringComparison.Ordinal))
                        continue;
                    ParameterInfo[] p = mi.GetParameters();
                    if (p.Length != 1)
                        continue;
                    object arg;
                    if (p[0].ParameterType == typeof(uint)) arg = Convert.ToUInt32(port);
                    else if (p[0].ParameterType == typeof(int)) arg = port;
                    else continue;
                    return mi.Invoke(mi.IsStatic ? null : instance, new[] { arg });
                }

                // Some builds expose only the single main server object.
                PropertyInfo portProp = instance.GetType().GetProperty("Port");
                if (portProp != null && Convert.ToInt32(portProp.GetValue(instance, null)) == port)
                    return instance;

                return instance;
            }
            catch (Exception e)
            {
                if (Verbose)
                    Log.Error("[DisplayNameCaps]: GetHttpServer failed", e);
                return null;
            }
        }

        internal sealed class CachedName
        {
            public string DisplayName = string.Empty;
            public DateTime NextAllowedUtc = DateTime.UtcNow;
            public DateTime ExpiresUtc = DateTime.MinValue;

            // True only when the Display Name web service explicitly returned
            // HTTP 404 for this UUID.  In /CAPS/agents this becomes bad_ids so
            // Firestorm can use the normal legacy/Hypergrid name cache.
            public bool ServiceNotFound = false;
        }

        private sealed class AgentCapsRouter
        {
            private readonly Scene _scene;
            private readonly UUID _agentId;

            public AgentCapsRouter(Scene scene, UUID agentId)
            {
                _scene = scene;
                _agentId = agentId;
            }

            public void HandleSet(IOSHttpRequest request, IOSHttpResponse response)
            {
                HandleWrite(request, response, false);
            }

            public void HandleReset(IOSHttpRequest request, IOSHttpResponse response)
            {
                HandleWrite(request, response, true);
            }

            private void HandleWrite(IOSHttpRequest request, IOSHttpResponse response, bool resetEndpoint)
            {
                string method = request.HttpMethod ?? string.Empty;
                bool allowed = method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                               method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
                               method.Equals("DELETE", StringComparison.OrdinalIgnoreCase);
                if (!allowed)
                {
                    response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    return;
                }

                string legacy = GetLegacyName(_scene, _agentId);
                CachedName before = GetCachedOrFetch(_agentId);
                string oldDisplay = EffectiveDisplay(before, legacy);

                bool requestReset = resetEndpoint || method.Equals("DELETE", StringComparison.OrdinalIgnoreCase);
                string requestedName = string.Empty;

                if (!requestReset)
                {
                    try
                    {
                        if (request.InputStream != null && request.InputStream.CanRead)
                        {
                            OSD root = OSDParser.DeserializeLLSDXml(request.InputStream);
                            if (root is OSDMap map && map.TryGetValue("display_name", out OSD dn))
                            {
                                // Firestorm commonly sends an array where item 1 is
                                // the actual requested display name.
                                if (dn is OSDArray arr && arr.Count >= 2)
                                    requestedName = arr[1].AsString();
                                else
                                    requestedName = dn.AsString();
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error("[DisplayNameCaps]: Cannot parse SetDisplayName request", e);
                    }

                    requestedName = (requestedName ?? string.Empty).Trim();
                    if (requestedName.Length > 64)
                        requestedName = requestedName.Substring(0, 64);

                    // Preserve the validated v4/Firestorm behavior: an empty
                    // SetDisplayName is a reset request.  The working v4 sends
                    // that reset through the regular /set service with name=""
                    // rather than switching to a separate backend operation.
                    if (requestedName.Length == 0)
                        requestReset = true;
                }

                // Keep write semantics aligned with the validated v4 module:
                // both a dedicated ResetDisplayName CAPS call and an empty
                // SetDisplayName are forwarded to /set with an empty name.
                // The companion backend interprets name="" as reset.
                bool ok = ServiceSet(_agentId, requestReset ? string.Empty : requestedName);
                ClearCache(_agentId);
                CachedName after = GetCachedOrFetch(_agentId);
                string effective = EffectiveDisplay(after, legacy);
                bool isDefault = IsDefaultDisplay(after, legacy);

                OSDMap content = BuildAgentMapForced(_agentId, legacy, effective, after.NextAllowedUtc, isDefault);
                content["display_name_set"] = OSD.FromBoolean(!isDefault);
                if (requestReset)
                    content["display_name_reset"] = OSD.FromBoolean(ok && isDefault);

                OSDMap reply = new OSDMap
                {
                    ["status"] = OSD.FromInteger(ok ? 200 : 500),
                    ["reason"] = OSD.FromString(ok ? string.Empty : "Display name service rejected the request"),
                    ["content"] = content
                };

                WriteLlsd(response, reply, ok ? 200 : 500);
                TrySendViewerEvents(_scene, _agentId, oldDisplay, content, ok);
            }

            private static void TrySendViewerEvents(Scene scene, UUID agentId, string oldDisplay, OSDMap agentMap, bool ok)
            {
                try
                {
                    IEventQueue eq = scene.RequestModuleInterface<IEventQueue>();
                    if (eq == null)
                        return;

                    OSDMap content = new OSDMap
                    {
                        ["status"] = OSD.FromInteger(ok ? 200 : 500),
                        ["reason"] = OSD.FromString(ok ? string.Empty : "Display name service rejected the request"),
                        ["content"] = agentMap
                    };
                    eq.Enqueue(eq.BuildEvent("SetDisplayNameReply", content), agentId);

                    OSDMap updateBody = new OSDMap
                    {
                        ["agent_id"] = OSD.FromUUID(agentId),
                        ["old_display_name"] = OSD.FromString(oldDisplay ?? string.Empty),
                        ["agent"] = agentMap
                    };
                    // The validated v4 builds a fresh EventQueue event for
                    // every recipient.  Do not reuse a single OSD event object:
                    // OpenSim/EventQueue may consume or mutate it per enqueue,
                    // which can prevent live updates reaching every viewer.
                    bool sent = false;
                    foreach (ScenePresence sp in EnumerateScenePresences(scene))
                    {
                        if (sp == null || sp.IsChildAgent)
                            continue;

                        eq.Enqueue(eq.BuildEvent("DisplayNameUpdate", updateBody), sp.UUID);
                        sent = true;
                    }
                    if (!sent)
                        eq.Enqueue(eq.BuildEvent("DisplayNameUpdate", updateBody), agentId);
                }
                catch (Exception e)
                {
                    if (Verbose)
                        Log.Error("[DisplayNameCaps]: viewer event enqueue failed", e);
                }
            }
        }

        private sealed class CapsAgentsRouter
        {
            private readonly Scene _scene;

            public CapsAgentsRouter(Scene scene)
            {
                _scene = scene;
            }

            public void HandleAgents(IOSHttpRequest request, IOSHttpResponse response)
            {
                if (!string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    return;
                }

                TrySetHeader(response, "Cache-Control", "max-age=5");

                OSDArray agents = new OSDArray();
                OSDArray badIds = new OSDArray();

                foreach (string raw in ExtractIds(request))
                {
                    if (!UUID.TryParse(raw, out UUID id))
                    {
                        badIds.Add(OSD.FromString(raw));
                        continue;
                    }

                    try
                    {
                        CachedName cached = GetCachedOrFetch(id);

                        // Do not claim foreign/unknown UUIDs as Display Name
                        // records.  The viewer's bad_ids path deliberately
                        // falls back to the legacy name cache, which is where
                        // OpenSim's HGUserManagementModule resolves Hypergrid
                        // identities.  Returning an agent map here with an
                        // invented legacy value would poison the viewer cache
                        // with "Unknown" entries.
                        if (cached != null && cached.ServiceNotFound)
                        {
                            badIds.Add(OSD.FromString(raw));
                            continue;
                        }

                        string legacy = GetLegacyName(_scene, id);
                        string display = EffectiveDisplay(cached, legacy);
                        bool isDefault = IsDefaultDisplay(cached, legacy);
                        agents.Add(BuildAgentMapForced(id, legacy, display, cached.NextAllowedUtc, isDefault));
                    }
                    catch
                    {
                        badIds.Add(OSD.FromString(raw));
                    }
                }

                OSDMap result = new OSDMap
                {
                    ["agents"] = agents,
                    ["bad_ids"] = badIds
                };
                WriteLlsd(response, result, 200);
            }

            private static IEnumerable<string> ExtractIds(IOSHttpRequest request)
            {
                try
                {
                    // QueryString is available on current OpenSim.  Reflection
                    // keeps this source tolerant of older 0.9.3.x shapes.
                    PropertyInfo qp = request.GetType().GetProperty("QueryString");
                    object q = qp?.GetValue(request, null);
                    if (q != null)
                    {
                        PropertyInfo item = q.GetType().GetProperty("Item", new[] { typeof(string) });
                        string value = item?.GetValue(q, new object[] { "ids" }) as string;
                        if (!string.IsNullOrWhiteSpace(value))
                            return SplitIds(value);
                    }
                }
                catch { }

                try
                {
                    string raw = GetRequestRawUrl(request);
                    int qmark = raw.IndexOf('?');
                    if (qmark >= 0)
                    {
                        string query = raw.Substring(qmark + 1);
                        foreach (string pair in query.Split('&'))
                        {
                            int eq = pair.IndexOf('=');
                            string key = eq >= 0 ? pair.Substring(0, eq) : pair;
                            string value = eq >= 0 ? pair.Substring(eq + 1) : string.Empty;
                            if (Uri.UnescapeDataString(key).Equals("ids", StringComparison.OrdinalIgnoreCase))
                                return SplitIds(Uri.UnescapeDataString(value));
                        }
                    }
                }
                catch { }

                return Array.Empty<string>();
            }

            private static IEnumerable<string> SplitIds(string ids)
            {
                return (ids ?? string.Empty)
                    .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0);
            }

            private static string GetRequestRawUrl(IOSHttpRequest request)
            {
                try
                {
                    foreach (string propName in new[] { "RawUrl", "RawURL", "Url", "URI" })
                    {
                        PropertyInfo p = request.GetType().GetProperty(propName,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        object v = p?.GetValue(request, null);
                        if (v != null)
                            return v.ToString() ?? string.Empty;
                    }
                }
                catch { }
                return string.Empty;
            }

            private static void TrySetHeader(IOSHttpResponse response, string name, string value)
            {
                try
                {
                    MethodInfo add = response.GetType().GetMethod("AddHeader",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(string), typeof(string) }, null);
                    if (add != null)
                    {
                        add.Invoke(response, new object[] { name, value });
                        return;
                    }
                }
                catch { }

                try
                {
                    PropertyInfo headers = response.GetType().GetProperty("Headers",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    object h = headers?.GetValue(response, null);
                    PropertyInfo item = h?.GetType().GetProperty("Item", new[] { typeof(string) });
                    item?.SetValue(h, value, new object[] { name });
                }
                catch { }
            }
        }

        private static string EffectiveDisplay(CachedName cached, string legacy)
        {
            return cached != null && !string.IsNullOrWhiteSpace(cached.DisplayName)
                ? cached.DisplayName.Trim()
                : legacy;
        }

        private static bool IsDefaultDisplay(CachedName cached, string legacy)
        {
            return cached == null || string.IsNullOrWhiteSpace(cached.DisplayName) ||
                   string.Equals(cached.DisplayName.Trim(), legacy?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static OSDMap BuildAgentMapForced(UUID id, string legacy, string display, DateTime nextAllowed, bool isDefault)
        {
            SplitLegacy(legacy, out string first, out string last);
            return new OSDMap
            {
                ["id"] = OSD.FromString(id.ToString()),
                ["display_name"] = OSD.FromString(display ?? legacy ?? string.Empty),
                ["username"] = OSD.FromString(UsernameFromLegacy(legacy)),
                ["is_display_name_default"] = OSD.FromBoolean(isDefault),
                ["display_name_expires"] = OSD.FromDate(DateTime.UtcNow.AddMinutes(5)),
                ["display_name_next_update"] = OSD.FromDate(nextAllowed.ToUniversalTime()),
                ["legacy_name"] = OSD.FromString(legacy ?? string.Empty),
                ["legacy_first_name"] = OSD.FromString(first),
                ["legacy_last_name"] = OSD.FromString(last),
                ["display_name_set"] = OSD.FromBoolean(!isDefault)
            };
        }

        private static string GetLegacyName(Scene scene, UUID id)
        {
            try
            {
                ScenePresence sp = scene.GetScenePresence(id);
                if (sp != null && !string.IsNullOrWhiteSpace(sp.Name))
                    return sp.Name;
            }
            catch { }

            // The recovered v4 deliberately used reflection here because the
            // user-account service surface has varied between OpenSim builds.
            try
            {
                object svc = null;
                PropertyInfo prop = scene.GetType().GetProperty("UserAccountService",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null)
                    svc = prop.GetValue(scene, null);

                if (svc != null)
                {
                    MethodInfo get = svc.GetType().GetMethods()
                        .FirstOrDefault(m =>
                        {
                            if (!string.Equals(m.Name, "GetUserAccount", StringComparison.Ordinal))
                                return false;
                            ParameterInfo[] p = m.GetParameters();
                            return p.Length == 2 && p[0].ParameterType == typeof(UUID) && p[1].ParameterType == typeof(UUID);
                        });

                    if (get != null)
                    {
                        UUID scope = UUID.Zero;
                        try { scope = scene.RegionInfo.ScopeID; } catch { }
                        object account = get.Invoke(svc, new object[] { scope, id });
                        if (account != null)
                        {
                            PropertyInfo name = account.GetType().GetProperty("Name");
                            string n = name?.GetValue(account, null)?.ToString();
                            if (!string.IsNullOrWhiteSpace(n))
                                return n.Trim();

                            string first = account.GetType().GetProperty("FirstName")?.GetValue(account, null)?.ToString() ?? string.Empty;
                            string last = account.GetType().GetProperty("LastName")?.GetValue(account, null)?.ToString() ?? string.Empty;
                            n = (first + " " + last).Trim();
                            if (n.Length > 0)
                                return n;
                        }
                    }
                }
            }
            catch { }

            return "Unknown Resident";
        }

        private static IEnumerable<ScenePresence> EnumerateScenePresences(Scene scene)
        {
            if (scene == null)
                yield break;

            // Preserve v4's compatibility strategy: prefer GetScenePresences(),
            // then fall back to a ScenePresences property.
            IEnumerable values = null;
            try
            {
                MethodInfo m = scene.GetType().GetMethod("GetScenePresences",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                values = m?.Invoke(scene, null) as IEnumerable;
            }
            catch { }

            if (values == null)
            {
                try
                {
                    PropertyInfo p = scene.GetType().GetProperty("ScenePresences",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    values = p?.GetValue(scene, null) as IEnumerable;
                }
                catch { }
            }

            if (values == null)
                yield break;

            foreach (object o in values)
                if (o is ScenePresence sp)
                    yield return sp;
        }
    }
}
