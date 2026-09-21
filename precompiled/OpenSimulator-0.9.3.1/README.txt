OpenSimulator Display Names — precompiled module for OpenSimulator 0.9.3.1

Files:
  DisplayNameSimModule.dll
  DisplayNameSimModule.pdb
  DisplayNameSimModule.addin.xml

Stop the simulator before installation, back up any previous files, copy these three files into the simulator bin directory, then rename/remove addin-db-004 while the simulator is stopped. Configure [DisplayNameCaps] in OpenSim.ini and add DisplayNameCapsModule to the existing [Modules] SharedRegionModules setting.

Full instructions: ../../docs/INSTALL_EN.md and ../../docs/INSTALL_FR.md
