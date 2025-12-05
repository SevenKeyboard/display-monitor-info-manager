#Requires AutoHotkey v1.1.17+
#Include %A_ScriptDir%
#Include .\lib\MonitorInfoExStruct.ahk
;==============================================================
; DisplayMonitorInfoManager — Monitor enumeration & cached display info manager
;
; GitHub: https://github.com/SevenKeyboard/display-monitor-info-manager
; Author: SevenKeyboard Ltd. (2025)
; License: MIT License
;==============================================================
class DisplayMonitorInfoManager
{
    static _ := DisplayMonitorManager._init()
    _init()    {
        global
        DISPLAYMONITORINFOMANAGER_VERSION := "1.0.0"
    }
    static _isInitialized:=false
    IsInitialized    {
        get  {
            return (!!this._isInitialized)
        }
    }
    getList(force:=false)    {
        if (!this._isInitialized)    {
            this.initialize()
        }  else if (force)    {
            this._info:=[]
            dllCall("User32.dll\EnumDisplayMonitors", "Ptr",0, "Ptr",0, "Ptr",this._lpfnEnum, "Ptr",0)
        }
        return this._info
    }
    initialize()    {
        static WM_SETTINGCHANGE:=0x001A, WM_DISPLAYCHANGE:=0x007E
        if (this._isInitialized)
            return
        this._isInitialized:=true
        this._info:=[]
        this._lpfnEnum:=registerCallback("displayMonitorManager_EnumDisplayMonitorsProc_A9763F99","Fast") ;  Routes to this.enumDisplayMonitorsProc()
        dllCall("User32.dll\EnumDisplayMonitors", "Ptr",0, "Ptr",0, "Ptr",this._lpfnEnum, "Ptr",0)
        onMessage(WM_SETTINGCHANGE, objBindMethod(this,"_onSettingChange"), -1) ;  onMessage(WM_DISPLAYCHANGE, objBindMethod(this,"_onDisplayChange"), -1)
    }
    _onSettingChange(wParam, lParam, msg, hwnd)    {
        static SPI_SETWORKAREA:=0x002F
        if (wParam==SPI_SETWORKAREA)    {
            this._info:=[]
            dllCall("User32.dll\EnumDisplayMonitors", "Ptr",0, "Ptr",0, "Ptr",this._lpfnEnum, "Ptr",0)
        }
    }
    /*
    _onDisplayChange(wParam, lParam, msg, hwnd)    {
        this._info:=[]
        dllCall("User32.dll\EnumDisplayMonitors", "Ptr",0, "Ptr",0, "Ptr",this._lpfnEnum, "Ptr",0)
    }
    */
    enumDisplayMonitorsProc(unnamedParam1, unnamedParam2, unnamedParam3, unnamedParam4)    { ;  HMONITOR, HDC, LPRECT, LPARAM
        if (A_PtrSize!==8)    {
            for i,_ in ["unnamedParam1","unnamedParam2","unnamedParam3","unnamedParam4"]
                %_%:=%_%<<32>>32
        }
        mi:=MONITORINFOEX("winuser.h")
        mi.setCbSize()
        if (dllCall("User32.dll\GetMonitorInfo", "Ptr",unnamedParam1, "Ptr",mi.Ptr))
            this._info.push(mi.getObj(unnamedParam1))
        return true
    }
}
displayMonitorManager_EnumDisplayMonitorsProc_A9763F99(unnamedParam1, unnamedParam2, unnamedParam3, unnamedParam4)    {
    return DisplayMonitorManager.enumDisplayMonitorsProc(unnamedParam1, unnamedParam2, unnamedParam3, unnamedParam4)
}