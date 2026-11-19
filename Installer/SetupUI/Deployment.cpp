#include "pch.h"
#include "Deployment.h"

#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.System.h>

using namespace winrt;
using namespace winrt::Windows::ApplicationModel;
using namespace winrt::Windows::Foundation;
using namespace winrt::Windows::Foundation::Collections;
using namespace winrt::Windows::Management::Deployment;

namespace
{
    uint64_t Pack(PackageVersion const& version)
    {
        return (static_cast<uint64_t>(version.Major) << 48) | (static_cast<uint64_t>(version.Minor) << 32) |
               (static_cast<uint64_t>(version.Build) << 16) | static_cast<uint64_t>(version.Revision);
    }

    // file:///C:/Some%20Folder/UltraPad.msixbundle
    Uri FileUri(std::wstring const& path)
    {
        std::wstring uri = L"file:///";
        size_t start = 0;
        bool first = true;
        while (start <= path.size())
        {
            size_t end = path.find(L'\\', start);
            if (end == std::wstring::npos) end = path.size();
            std::wstring segment = path.substr(start, end - start);
            if (!first) uri += L'/';
            uri += first ? segment : std::wstring(Uri::EscapeComponent(segment));
            first = false;
            start = end + 1;
        }
        return Uri(uri);
    }

    bool AlreadyInstalled(PackageManager const& manager, setup::PackageFile const& dependency)
    {
        for (auto const& package : manager.FindPackagesForUser(L"", dependency.Name, dependency.Publisher))
        {
            auto id = package.Id();
            if (static_cast<uint32_t>(id.Architecture()) == dependency.Architecture && Pack(id.Version()) >= dependency.Version)
            {
                return true;
            }
        }
        return false;
    }
}

namespace setup
{
    InstalledState FindInstalled(std::wstring const& familyName)
    {
        InstalledState state;
        PackageManager manager;
        for (auto const& package : manager.FindPackagesForUser(L"", familyName))
        {
            uint64_t version = Pack(package.Id().Version());
            if (version >= state.Version)
            {
                state.Version = version;
                state.Kind = package.SignatureKind() == PackageSignatureKind::Store ? InstalledKind::Store : InstalledKind::Sideloaded;
            }
            state.FullNames.push_back(package.Id().FullName());
        }
        Log(L"Installed version: %s", state.Kind == InstalledKind::None ? L"none" : VersionString(state.Version).c_str());
        return state;
    }

    DeploymentOperation AddPackage(Payload const& payload)
    {
        PackageManager manager;

        auto dependencies = single_threaded_vector<Uri>();
        for (auto const& dependency : payload.Dependencies)
        {
            if (AlreadyInstalled(manager, dependency))
            {
                Log(L"%s %s or newer is already installed", dependency.Name.c_str(), VersionString(dependency.Version).c_str());
                continue;
            }
            dependencies.Append(FileUri(dependency.Path));
        }

        Log(L"Installing %s with %u dependencies", payload.App.Path.c_str(), dependencies.Size());
        IVector<Uri> list = dependencies.Size() ? dependencies : IVector<Uri>{ nullptr };
        return manager.AddPackageAsync(FileUri(payload.App.Path), list,
                                       DeploymentOptions::ForceApplicationShutdown | DeploymentOptions::ForceUpdateFromAnyVersion);
    }

    DeploymentOperation RemovePackage(hstring const& fullName)
    {
        Log(L"Removing %s", fullName.c_str());
        PackageManager manager;
        return manager.RemovePackageAsync(fullName);
    }

    std::wstring AppUserModelId(Payload const& payload)
    {
        return payload.App.FamilyName + L"!App";
    }

    std::wstring DescribeError(DeploymentResult const& result, long fallback)
    {
        long code = result ? static_cast<long>(result.ExtendedErrorCode().value) : fallback;
        std::wstring text = result ? std::wstring(result.ErrorText()) : std::wstring();
        if (text.empty()) text = hresult_error(code).message();

        Log(L"Deployment failed: 0x%08X %s", static_cast<unsigned>(code), text.c_str());
        Log(L"More details: Event Viewer > Applications and Services Logs > Microsoft > Windows > AppXDeployment-Server");
        return text;
    }

    int RunQuiet(bool uninstall)
    {
        Log(L"Quiet %s", uninstall ? L"uninstall" : L"install");
        try
        {
            Payload payload;
            HRESULT hr = LoadPayload(payload);
            if (FAILED(hr)) return hr;

            if (uninstall)
            {
                RemoveLegacyMsi();
                for (auto const& fullName : FindInstalled(payload.App.FamilyName).FullNames)
                {
                    DeploymentResult result = RemovePackage(fullName).get();
                    if (FAILED(result.ExtendedErrorCode().value))
                    {
                        DescribeError(result, E_FAIL);
                        return result.ExtendedErrorCode().value;
                    }
                }
                RemoveOurCertificate();
                Log(L"Uninstalled");
                return 0;
            }

            if (FindInstalled(payload.App.FamilyName).Kind == InstalledKind::Store)
            {
                Log(L"The Microsoft Store version is installed; nothing to do");
                return HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS);
            }

            RemoveLegacyMsi();
            AllowSideloading();
            bool added = false;
            if (FAILED(hr = TrustCertificate(payload, added))) return hr;

            DeploymentResult result = AddPackage(payload).get();
            if (FAILED(result.ExtendedErrorCode().value))
            {
                DescribeError(result, E_FAIL);
                if (added) RemoveOurCertificate();
                return result.ExtendedErrorCode().value;
            }
            Log(L"Installed");
            return 0;
        }
        catch (hresult_error const& error)
        {
            Log(L"0x%08X %s", static_cast<unsigned>(error.code().value), error.message().c_str());
            return error.code().value;
        }
    }
}
