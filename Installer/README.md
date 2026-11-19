# UltraPad Setup

`UltraPad-Setup.exe` installs the sideloaded UltraPad package without PowerShell, MSI or WiX. It is the alternative to the Microsoft Store version.

What it does:

- Asks for administrator rights once, when it starts. Nothing else asks again.
- Shows a WinUI 3 window with Mica that has install, update, reinstall and uninstall pages, a progress bar and a completion screen.
- Adds the signing certificate to *Trusted People* only when Windows doesn't already trust the package. It remembers that it did so, so uninstalling removes the certificate again.
- Installs only the dependencies that aren't already installed in the same or a newer version.
- Doesn't create its own entry in *Installed apps*. The only entry is the package's own, so uninstalling from Settings or from the installer leaves nothing behind. That includes *Open with*, the Start menu and file associations.
- Removes the entry left by the old Appx2Msi installer, if there is one.
- Notices when the Microsoft Store version is installed and doesn't try to install over it.

## Building

Requirements: Visual Studio 2022 or 2026 with *Desktop development with C++* and *WinUI application development*. NuGet restores the Windows App SDK on the first build.

1. Create the sideloading package of UltraPad: *Publish > Create App Packages > Sideloading*.
2. Copy the `.msixbundle`, the `.cer` and the `Dependencies` folder from `WordPad\AppPackages\WordPad_<version>_Test\` into `Installer\Payload\`. See `Payload\README.txt` for details.
3. Open `Installer\UltraPadSetup.sln`, select **Release | x64** and build the solution.
4. The installer is `Installer\bin\x64\Release\UltraPad-Setup.exe`. It is the only file you need to publish.

For ARM64 PCs, put the ARM64 dependencies into `Payload\Dependencies\arm64\` and build **Release | ARM64** as well.

The version in the file's properties comes from `Version.props` in the repository root, the same file that sets the app's version. The installer window reads the version from the package.

## How it works

| Project | What it is |
|---|---|
| `SetupUI` | The installer window. WinUI 3 and C++/WinRT, unpackaged, with the Windows App SDK runtime self-contained so nothing has to be installed first. Uses `PackageManager` directly. |
| `Packer` | A build tool that packs the SetupUI output and `Payload` into one LZMS-compressed archive. It always builds as x64. |
| `Setup` | `UltraPad-Setup.exe`, a small Win32 stub that carries the archive as a resource. It unpacks the archive to `%TEMP%`, runs SetupUI, waits for it and deletes the temporary folder. |

## Command line

| Switch | Effect |
|---|---|
| *(none)* | Normal installer window. |
| `/uninstall` | Opens the window and uninstalls right away. |
| `/quiet` | Installs without any window. Returns 0 on success, otherwise an HRESULT. |
| `/quiet /uninstall` | Uninstalls without any window. |

The log is written to `%TEMP%\UltraPadSetup.log` of the administrator account. The error page has a link that opens it. Deployment errors also appear in Event Viewer under *Applications and Services Logs > Microsoft > Windows > AppXDeployment-Server*.

## Notes

- The app is installed for the account that confirms the UAC prompt. When a standard user enters an administrator's password, UltraPad is installed for that administrator account, which is the same as with `Add-AppxPackage`.
- The old `Appx2Msi` folder isn't needed by this installer and can be removed from the repository.
