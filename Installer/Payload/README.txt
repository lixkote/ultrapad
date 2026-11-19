Put the files that UltraPad-Setup.exe installs here before building the solution:

  UltraPad_<version>_x64.msixbundle      the app (any .msixbundle, .appxbundle, .msix or .appx)
  UltraPad_<version>_x64.cer             optional: the certificate the package is signed with
  Dependencies\x64\*.appx                the framework packages from the AppPackages folder
  Dependencies\arm64\*.appx              (one folder per architecture you want to support)

Visual Studio creates all of these in WordPad\AppPackages\WordPad_<version>_Test\ when you
run Publish > Create App Packages > Sideloading. Copy the .msixbundle, the .cer and the whole
Dependencies folder as they are.
