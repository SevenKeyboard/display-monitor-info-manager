#Requires AutoHotkey v2.0.0+
#Include "%A_ScriptDir%"
#Include ".\lib\MonitorInfoExStruct.ahk"
;==============================================================
; DisplayMonitorInfoManager — Monitor enumeration & cached display info manager
;
; GitHub: https://github.com/SevenKeyboard/display-monitor-info-manager
; Author: SevenKeyboard Ltd. (2025)
; License: MIT License
;==============================================================
class VersionManager_DisplayMonitorInfoManager
{
    static _ := this._init()
    static _init()    {
        global
        DISPLAYMONITORINFOMANAGER_VERSION := "1.0.0"
    }
}
class DisplayMonitorInfoManager
{
    static _isInitialized:=false
    static IsInitialized    {
        get  {
            return (!!this._isInitialized)
        }
    }
    static getList(force:=false)    {
        if (!this._isInitialized)    {
            this.initialize()
        }  else if (force)    {
            this._info:=[]
            dllCall("User32.dll\EnumDisplayMonitors", "Ptr",0, "Ptr",0, "Ptr",this._lpfnEnum, "Ptr",0)
        }
        return this._info
    }
    static initialize()    {
        static WM_SETTINGCHANGE:=0x001A, WM_DISPLAYCHANGE:=0x007E
        if (this._isInitialized)
            return
        this._isInitialized:=true
        this._info:=[]
        this._lpfnEnum:=callbackCreate(objBindMethod(this,"_enumDisplayMonitorsProc"),"Fast",4)
        dllCall("User32.dll\EnumDisplayMonitors", "Ptr",0, "Ptr",0, "Ptr",this._lpfnEnum, "Ptr",0)
        onMessage(WM_SETTINGCHANGE, objBindMethod(this,"_onSettingChange"), -1) ;  onMessage(WM_DISPLAYCHANGE, objBindMethod(this,"_onDisplayChange"), -1)
    }
    static _onSettingChange(wParam, lParam, msg, hwnd)    {
        static SPI_SETWORKAREA:=0x002F
        if (wParam==SPI_SETWORKAREA)    {
            this._info:=[]
            dllCall("User32.dll\EnumDisplayMonitors", "Ptr",0, "Ptr",0, "Ptr",this._lpfnEnum, "Ptr",0)
        }
    }
    /*
    static _onDisplayChange(wParam, lParam, msg, hwnd)    {
        this._info:=[]
        dllCall("User32.dll\EnumDisplayMonitors", "Ptr",0, "Ptr",0, "Ptr",this._lpfnEnum, "Ptr",0)
    }
    */
    static _enumDisplayMonitorsProc(unnamedParam1, unnamedParam2, unnamedParam3, unnamedParam4)    { ;  HMONITOR, HDC, LPRECT, LPARAM
        if (A_PtrSize!==8)    {
            for _ in [&unnamedParam1,&unnamedParam2,&unnamedParam3,&unnamedParam4]
                %_%:=%_%<<32>>32
        }
        mi:=MONITORINFOEXW("winuser.h")
        mi.setCbSize()
        if (dllCall("User32.dll\GetMonitorInfo", "Ptr",unnamedParam1, "Ptr",mi.Ptr))
            this._info.push(mi.getObj(unnamedParam1))
        return true
    }
}