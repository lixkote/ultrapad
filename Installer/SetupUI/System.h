#pragma once

// Plain Win32 helpers used by the installer. Nothing in here depends on C++/WinRT or XAML.

#include <cstdint>
#include <string>
#include <vector>

namespace setup
{
    struct PackageFile
    {
        std::wstring Path;
        std::wstring Name;
        std::wstring Publisher;
        std::wstring FamilyName;
        uint64_t Version = 0;
        uint32_t Architecture = 0; // APPX_PACKAGE_ARCHITECTURE, same values as Windows::System::ProcessorArchitecture
    };

    struct Payload
    {
        PackageFile App;
        std::vector<PackageFile> Dependencies;
        std::wstring Certificate;
    };

    // folder that SetupUI.exe runs from
    std::wstring ModuleFolder();

    // finds the package, the optional certificate and the dependencies for this PC's architecture
    // in <ModuleFolder>\Payload; returns an HRESULT
    long LoadPayload(Payload& payload);

    std::wstring VersionString(uint64_t version);
    std::wstring HResultString(long hr);
    std::wstring Format(wchar_t const* text, std::wstring const& a, std::wstring const& b = {});

    // makes Windows trust the package signature when it doesn't already;
    // remembers if the certificate was added by us, so uninstalling can remove it again
    long TrustCertificate(Payload const& payload, bool& added);
    void RemoveOurCertificate();

    // sideloading is on by default since Windows 10 2004; only an explicit policy of 0 blocks it
    void AllowSideloading();

    // removes the entry that the old MSI installer (Appx2Msi) left in Installed apps
    void RemoveLegacyMsi();

    // starts the app as the signed-in user (not elevated), through the shell
    void LaunchApp(std::wstring const& appUserModelId);

    // true for /name or -name on the command line, case-insensitive
    bool HasSwitch(wchar_t const* name);

    // what the process returns when the window closes
    int& ExitCode();

    void Log(wchar_t const* format, ...);
    std::wstring LogPath();
}
