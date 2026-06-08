#include <Windows.h>

namespace
{
    constexpr UINT SmartLangActivateLayout = WM_APP + 0x534;
}

extern "C" __declspec(dllexport) LRESULT CALLBACK SmartLangGetMessageHook(
    int code,
    WPARAM wParam,
    LPARAM lParam)
{
    if (code >= 0 && lParam != 0)
    {
        auto* message = reinterpret_cast<MSG*>(lParam);
        if (message->message == SmartLangActivateLayout)
        {
            ActivateKeyboardLayout(reinterpret_cast<HKL>(message->wParam), 0);
            message->message = WM_NULL;
            message->wParam = 0;
            message->lParam = 0;
            return 0;
        }
    }

    return CallNextHookEx(nullptr, code, wParam, lParam);
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID)
{
    return TRUE;
}
