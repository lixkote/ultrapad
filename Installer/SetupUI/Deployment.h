#pragma once

#include "System.h"

#include <winrt/Windows.ApplicationModel.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Management.Deployment.h>

namespace setup
{
    enum class InstalledKind
    {
        None,
        Sideloaded,
        Store,
    };

    struct InstalledState
    {
        InstalledKind Kind = InstalledKind::None;
        uint64_t Version = 0;
        std::vector<winrt::hstring> FullNames;
    };

    using DeploymentOperation = winrt::Windows::Foundation::IAsyncOperationWithProgress<
        winrt::Windows::Management::Deployment::DeploymentResult,
        winrt::Windows::Management::Deployment::DeploymentProgress>;

    InstalledState FindInstalled(std::wstring const& familyName);

    // installs (or updates) the app together with the dependencies that aren't installed yet
    DeploymentOperation AddPackage(Payload const& payload);
    DeploymentOperation RemovePackage(winrt::hstring const& fullName);

    std::wstring AppUserModelId(Payload const& payload);

    // the error of a finished deployment as text for the user
    std::wstring DescribeError(winrt::Windows::Management::Deployment::DeploymentResult const& result, long fallback);

    // /quiet and /quiet /uninstall: no window, the result is the exit code
    int RunQuiet(bool uninstall);
}
