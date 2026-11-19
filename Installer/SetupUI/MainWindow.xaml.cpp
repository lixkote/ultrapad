#include "pch.h"
#include "MainWindow.xaml.h"
#if __has_include("MainWindow.g.cpp")
#include "MainWindow.g.cpp"
#endif

#include "Strings.h"

using namespace winrt;
using namespace winrt::Microsoft::UI::Dispatching;
using namespace winrt::Microsoft::UI::Windowing;
using namespace winrt::Microsoft::UI::Xaml;
using namespace winrt::Microsoft::UI::Xaml::Controls;
using namespace winrt::Windows::Foundation;
using namespace winrt::Windows::Management::Deployment;

namespace
{
    // co_await resume_ui{ queue } continues on the window's thread
    struct resume_ui
    {
        DispatcherQueue queue;

        bool await_ready() const { return queue.HasThreadAccess(); }

        void await_suspend(std::coroutine_handle<> handle) const
        {
            if (!queue.TryEnqueue([handle] { handle(); }))
            {
                throw hresult_error(RPC_E_DISCONNECTED);
            }
        }

        void await_resume() const {}
    };

    constexpr int WindowWidth = 600;
    constexpr int WindowHeight = 380;
    constexpr int ExitCancelled = 1602; // same code msiexec uses when the user cancels
}

namespace winrt::SetupUI::implementation
{
    MainWindow::MainWindow()
    {
        InitializeComponent();
        m_queue = DispatcherQueue();

        auto const& text = setup::Text();
        TitleText().Text(text.WindowTitle);
        LaunchCheck().Content(box_value(text.LaunchApp));
        LogButton().Content(box_value(text.OpenLog));

        SetupWindow();
        setup::ExitCode() = ExitCancelled;

        ShowWorking(text.WindowTitle, text.Preparing);
        LoadAsync();
    }

    void MainWindow::SetupWindow()
    {
        Title(setup::Text().WindowTitle);
        ExtendsContentIntoTitleBar(true);
        SetTitleBar(AppTitleBar());
        SystemBackdrop(Media::MicaBackdrop());

        auto appWindow = AppWindow();
        appWindow.SetIcon(setup::ModuleFolder() + L"\\Assets\\UltraPad.ico");

        OverlappedPresenter presenter = OverlappedPresenter::Create();
        presenter.IsResizable(false);
        presenter.IsMaximizable(false);
        appWindow.SetPresenter(presenter);

        HWND hwnd = nullptr;
        Window window = *this;
        window.as<::IWindowNative>()->get_WindowHandle(&hwnd);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        int width = static_cast<int>(WindowWidth * scale);
        int height = static_cast<int>(WindowHeight * scale);

        auto area = DisplayArea::GetFromWindowId(appWindow.Id(), DisplayAreaFallback::Nearest).WorkArea();
        appWindow.MoveAndResize({ area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height });

        // installing can't be interrupted halfway
        appWindow.Closing([weak = get_weak()](auto const&, AppWindowClosingEventArgs const& args)
        {
            if (auto self = weak.get(); self && self->m_busy) args.Cancel(true);
        });
    }

    fire_and_forget MainWindow::LoadAsync()
    {
        auto strong = get_strong();
        auto queue = m_queue;

        co_await resume_background();
        setup::Payload payload;
        setup::InstalledState installed;
        long hr = setup::LoadPayload(payload);
        if (SUCCEEDED(hr))
        {
            try
            {
                installed = setup::FindInstalled(payload.App.FamilyName);
            }
            catch (hresult_error const& error)
            {
                setup::Log(L"Could not look for an installed version: 0x%08X", static_cast<unsigned>(error.code().value));
            }
        }

        co_await resume_ui{ queue };
        auto const& text = setup::Text();
        if (FAILED(hr))
        {
            ShowError(text.InstallFailed, text.PayloadMissing, hr, false);
            co_return;
        }

        m_payload = std::move(payload);
        m_installed = std::move(installed);

        if (setup::HasSwitch(L"uninstall"))
        {
            if (m_installed.Kind == setup::InstalledKind::None)
            {
                ShowDone(true);
            }
            else
            {
                UninstallAsync(false);
            }
            co_return;
        }
        ShowWelcome();
    }

    void MainWindow::ShowWelcome()
    {
        auto const& text = setup::Text();
        m_page = Page::Welcome;

        std::wstring available = setup::VersionString(m_payload.App.Version);
        std::wstring current = setup::VersionString(m_installed.Version);
        wchar_t const* heading = text.InstallTitle;
        std::wstring body;
        wchar_t const* primary = nullptr;
        bool canUninstall = true;

        if (m_installed.Kind == setup::InstalledKind::None)
        {
            body = setup::Format(text.InstallText, available) + L"\n\n" + text.Description;
            primary = text.Install;
            canUninstall = false;
        }
        else if (m_installed.Kind == setup::InstalledKind::Store)
        {
            heading = text.StoreTitle;
            body = text.StoreText;
        }
        else if (m_installed.Version < m_payload.App.Version)
        {
            heading = text.UpdateTitle;
            body = setup::Format(text.UpdateText, current, available);
            primary = text.Update;
        }
        else if (m_installed.Version == m_payload.App.Version)
        {
            heading = text.InstalledTitle;
            body = setup::Format(text.InstalledText, current);
            primary = text.Reinstall;
        }
        else
        {
            heading = text.NewerTitle;
            body = setup::Format(text.NewerText, current, available);
            primary = text.Downgrade;
        }

        HeadingText().Text(heading);
        BodyText().Text(body);
        BodyText().Visibility(Visibility::Visible);
        ProgressPanel().Visibility(Visibility::Collapsed);
        LaunchCheck().Visibility(Visibility::Collapsed);
        ErrorBar().IsOpen(false);
        DoneBadge().Visibility(Visibility::Collapsed);

        SetButton(PrimaryButton(), primary);
        SetButton(SecondaryButton(), canUninstall ? text.Uninstall : nullptr);
        SetButton(CloseButton(), text.Cancel);

        if (primary) PrimaryButton().Focus(FocusState::Programmatic);
    }

    void MainWindow::ShowWorking(wchar_t const* heading, wchar_t const* status)
    {
        m_page = Page::Working;
        HeadingText().Text(heading);
        BodyText().Text(L"");
        BodyText().Visibility(Visibility::Collapsed);
        LaunchCheck().Visibility(Visibility::Collapsed);
        ErrorBar().IsOpen(false);
        DoneBadge().Visibility(Visibility::Collapsed);

        ProgressPanel().Visibility(Visibility::Visible);
        Progress().IsIndeterminate(true);
        Progress().Value(0);
        ShowStatus(status);

        SetButton(PrimaryButton(), nullptr);
        SetButton(SecondaryButton(), nullptr);
        SetButton(CloseButton(), setup::Text().Cancel);
        CloseButton().IsEnabled(false);
    }

    void MainWindow::ShowStatus(wchar_t const* status)
    {
        m_status = status;
        StatusText().Text(status);
    }

    void MainWindow::ShowProgress(uint32_t percentage)
    {
        if (percentage == 0) return;
        Progress().IsIndeterminate(false);
        Progress().Value(static_cast<double>(percentage));
        StatusText().Text(std::wstring(m_status) + L"  " + std::to_wstring(percentage) + L"%");
    }

    void MainWindow::ShowDone(bool uninstalled)
    {
        auto const& text = setup::Text();
        m_page = Page::Done;
        m_uninstalled = uninstalled;
        setup::ExitCode() = 0;

        HeadingText().Text(uninstalled ? text.UninstalledTitle : text.DoneTitle);
        BodyText().Text(uninstalled ? text.UninstalledText : text.DoneText);
        BodyText().Visibility(Visibility::Visible);
        ProgressPanel().Visibility(Visibility::Collapsed);
        ErrorBar().IsOpen(false);
        LaunchCheck().Visibility(uninstalled ? Visibility::Collapsed : Visibility::Visible);
        DoneBadge().Visibility(uninstalled ? Visibility::Collapsed : Visibility::Visible);

        SetButton(PrimaryButton(), text.Finish);
        SetButton(SecondaryButton(), nullptr);
        SetButton(CloseButton(), nullptr);
        PrimaryButton().Focus(FocusState::Programmatic);
    }

    void MainWindow::ShowError(wchar_t const* heading, std::wstring const& message, long code, bool canRetry)
    {
        auto const& text = setup::Text();
        m_page = Page::Error;
        setup::ExitCode() = code;

        HeadingText().Text(heading);
        BodyText().Visibility(Visibility::Collapsed);
        ProgressPanel().Visibility(Visibility::Collapsed);
        LaunchCheck().Visibility(Visibility::Collapsed);
        DoneBadge().Visibility(Visibility::Collapsed);

        ErrorBar().Title(L"");
        ErrorBar().Message(message + L"\n" + setup::Format(text.ErrorCode, setup::HResultString(code)));
        ErrorBar().IsOpen(true);

        SetButton(PrimaryButton(), canRetry ? text.TryAgain : nullptr);
        SetButton(SecondaryButton(), nullptr);
        SetButton(CloseButton(), text.Close);
    }

    void MainWindow::SetButton(Button const& button, wchar_t const* text)
    {
        button.IsEnabled(true);
        button.Visibility(text ? Visibility::Visible : Visibility::Collapsed);
        if (text) button.Content(box_value(text));
    }

    fire_and_forget MainWindow::InstallAsync()
    {
        auto strong = get_strong();
        auto queue = m_queue;
        auto const& text = setup::Text();

        m_busy = true;
        ShowWorking(m_installed.Kind == setup::InstalledKind::None ? text.InstallingTitle : text.UpdatingTitle, text.Preparing);

        co_await resume_background();
        setup::RemoveLegacyMsi();
        setup::AllowSideloading();

        co_await resume_ui{ queue };
        ShowStatus(text.Certificate);
        co_await resume_background();

        bool addedCertificate = false;
        long code = setup::TrustCertificate(m_payload, addedCertificate);
        DeploymentResult result{ nullptr };

        // without the certificate Windows reports a clearer error than we could
        try
        {
            auto operation = setup::AddPackage(m_payload);
            operation.Progress([weak = get_weak(), queue](auto const&, DeploymentProgress const& progress)
            {
                uint32_t percentage = progress.percentage;
                queue.TryEnqueue([weak, percentage]
                {
                    if (auto self = weak.get()) self->ShowProgress(percentage);
                });
            });

            queue.TryEnqueue([weak = get_weak(), status = text.Installing]
            {
                if (auto self = weak.get()) self->ShowStatus(status);
            });

            result = co_await operation;
            code = result.ExtendedErrorCode().value;
            if (SUCCEEDED(code) && operation.Status() == AsyncStatus::Error) code = operation.ErrorCode().value;
        }
        catch (hresult_error const& error)
        {
            code = error.code().value;
        }

        if (FAILED(code) && addedCertificate) setup::RemoveOurCertificate();
        std::wstring message = FAILED(code) ? setup::DescribeError(result, code) : std::wstring();
        if (SUCCEEDED(code)) setup::Log(L"Installed");

        co_await resume_ui{ queue };
        m_busy = false;
        if (FAILED(code))
        {
            ShowError(text.InstallFailed, message, code, true);
        }
        else
        {
            ShowDone(false);
        }
    }

    fire_and_forget MainWindow::UninstallAsync(bool confirm)
    {
        auto strong = get_strong();
        auto queue = m_queue;
        auto const& text = setup::Text();

        if (confirm)
        {
            ContentDialog dialog;
            dialog.XamlRoot(Content().XamlRoot());
            dialog.Style(Application::Current().Resources().Lookup(box_value(L"DefaultContentDialogStyle")).as<Style>());
            dialog.Title(box_value(text.ConfirmUninstall));
            dialog.Content(box_value(text.ConfirmUninstallText));
            dialog.PrimaryButtonText(text.Uninstall);
            dialog.CloseButtonText(text.Cancel);
            dialog.DefaultButton(ContentDialogButton::Close);
            if (co_await dialog.ShowAsync() != ContentDialogResult::Primary) co_return;
        }

        m_busy = true;
        ShowWorking(text.UninstallingTitle, text.Removing);

        co_await resume_background();
        long code = S_OK;
        DeploymentResult result{ nullptr };
        try
        {
            setup::RemoveLegacyMsi();
            for (auto const& fullName : setup::FindInstalled(m_payload.App.FamilyName).FullNames)
            {
                auto operation = setup::RemovePackage(fullName);
                operation.Progress([weak = get_weak(), queue](auto const&, DeploymentProgress const& progress)
                {
                    uint32_t percentage = progress.percentage;
                    queue.TryEnqueue([weak, percentage]
                    {
                        if (auto self = weak.get()) self->ShowProgress(percentage);
                    });
                });
                result = co_await operation;
                code = result.ExtendedErrorCode().value;
                if (SUCCEEDED(code) && operation.Status() == AsyncStatus::Error) code = operation.ErrorCode().value;
                if (FAILED(code)) break;
            }
        }
        catch (hresult_error const& error)
        {
            code = error.code().value;
        }

        std::wstring message;
        if (SUCCEEDED(code))
        {
            co_await resume_ui{ queue };
            ShowStatus(text.Cleaning);
            co_await resume_background();
            setup::RemoveOurCertificate();
            setup::Log(L"Uninstalled");
        }
        else
        {
            message = setup::DescribeError(result, code);
        }

        co_await resume_ui{ queue };
        m_busy = false;
        m_installed = {};
        if (FAILED(code))
        {
            ShowError(text.UninstallFailed, message, code, false);
        }
        else
        {
            ShowDone(true);
        }
    }

    void MainWindow::PrimaryButton_Click(IInspectable const&, RoutedEventArgs const&)
    {
        switch (m_page)
        {
        case Page::Welcome:
            InstallAsync();
            break;

        case Page::Done:
            if (!m_uninstalled && unbox_value_or<bool>(LaunchCheck().IsChecked(), false))
            {
                setup::LaunchApp(setup::AppUserModelId(m_payload));
            }
            Close();
            break;

        case Page::Error:
            ShowWorking(setup::Text().WindowTitle, setup::Text().Preparing);
            LoadAsync();
            break;

        default:
            break;
        }
    }

    void MainWindow::SecondaryButton_Click(IInspectable const&, RoutedEventArgs const&)
    {
        if (m_page == Page::Welcome) UninstallAsync(true);
    }

    void MainWindow::CloseButton_Click(IInspectable const&, RoutedEventArgs const&)
    {
        if (!m_busy) Close();
    }

    void MainWindow::LogButton_Click(IInspectable const&, RoutedEventArgs const&)
    {
        wchar_t windows[MAX_PATH];
        GetWindowsDirectoryW(windows, MAX_PATH);
        std::wstring notepad = std::wstring(windows) + L"\\notepad.exe";
        std::wstring arguments = L"\"" + setup::LogPath() + L"\"";
        ShellExecuteW(nullptr, nullptr, notepad.c_str(), arguments.c_str(), nullptr, SW_SHOWNORMAL);
    }
}
