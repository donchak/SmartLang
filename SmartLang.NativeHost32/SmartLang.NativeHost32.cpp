#include <Windows.h>
#include <Shellapi.h>

#include <cwchar>
#include <string>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    int argumentCount = 0;
    auto arguments = CommandLineToArgvW(GetCommandLineW(), &argumentCount);
    if (arguments == nullptr || argumentCount != 3)
    {
        return ERROR_INVALID_PARAMETER;
    }

    const std::wstring hookPath = arguments[1];
    wchar_t* end = nullptr;
    const auto parentProcessId = wcstoul(arguments[2], &end, 10);
    const auto processIdIsValid =
        parentProcessId != 0 && end != nullptr && *end == L'\0';
    LocalFree(arguments);
    if (!processIdIsValid)
    {
        return ERROR_INVALID_PARAMETER;
    }

    const auto parentProcess = OpenProcess(SYNCHRONIZE, FALSE, parentProcessId);
    if (parentProcess == nullptr)
    {
        return static_cast<int>(GetLastError());
    }

    const auto module = LoadLibraryExW(
        hookPath.c_str(),
        nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (module == nullptr)
    {
        const auto error = GetLastError();
        CloseHandle(parentProcess);
        return static_cast<int>(error);
    }

    auto hookProcedure = reinterpret_cast<HOOKPROC>(
        GetProcAddress(module, "SmartLangGetMessageHook"));
    if (hookProcedure == nullptr)
    {
        hookProcedure = reinterpret_cast<HOOKPROC>(
            GetProcAddress(module, "_SmartLangGetMessageHook@12"));
    }

    if (hookProcedure == nullptr)
    {
        const auto error = GetLastError();
        FreeLibrary(module);
        CloseHandle(parentProcess);
        return static_cast<int>(error);
    }

    const auto hook = SetWindowsHookExW(
        WH_GETMESSAGE,
        hookProcedure,
        module,
        0);
    if (hook == nullptr)
    {
        const auto error = GetLastError();
        FreeLibrary(module);
        CloseHandle(parentProcess);
        return static_cast<int>(error);
    }

    MSG message{};
    while (true)
    {
        const auto waitResult = MsgWaitForMultipleObjects(
            1,
            &parentProcess,
            FALSE,
            INFINITE,
            QS_ALLINPUT);
        if (waitResult == WAIT_OBJECT_0)
        {
            break;
        }

        if (waitResult != WAIT_OBJECT_0 + 1)
        {
            break;
        }

        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE))
        {
            if (message.message == WM_QUIT)
            {
                break;
            }

            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }

    UnhookWindowsHookEx(hook);
    FreeLibrary(module);
    CloseHandle(parentProcess);
    return 0;
}
