namespace RTOpenAI.WebRTC
// open Microsoft.Maui.Devices
// open Microsoft.Maui.Controls.PlatformConfiguration

module WebRtc = 

    let create() : IWebRtcClient =
                #if WINDOWS
                    new RTOpenAI.WebRTC.Windows.WebRtcClientWin()
                #else
                    failwith "WebRTC not supported on this platform"
                #endif
