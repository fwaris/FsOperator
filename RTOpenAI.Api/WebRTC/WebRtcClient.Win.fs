namespace RTOpenAI.WebRTC.Windows
#if WINDOWS
open RTOpenAI.WebRTC
open System
open System.Threading
open System.Threading.Tasks
open SIPSorcery.Net
open System.Text.Json
open RTOpenAI.Api
open SIPSorcery.Media
open SIPSorcery.Net
open SIPSorceryMedia.Windows
open SIPSorceryMedia.Abstractions

module Connect =
    let createPeerConnection (config:RTCConfiguration) =
        try
            new RTCPeerConnection(config)
        with ex ->
            Log.exn(ex,"unable to create peer connection")
            raise ex
    
    let createAudioTrack  (pc:RTCPeerConnection) = 
        let winAudioEP = new WindowsAudioEndPoint(new AudioEncoder(includeOpus=true), -1, 1, false, false)
        winAudioEP.RestrictFormats(fun x -> x.FormatName = "OPUS") 
        winAudioEP.add_OnAudioSinkError(SourceErrorDelegate(fun err -> Log.error $"Audio sink error {err}"))
        winAudioEP.add_OnAudioSourceEncodedSample(EncodedSampleDelegate(fun duration bytes -> pc.SendAudio(duration,bytes)))
        let audioTrack = new MediaStreamTrack(winAudioEP.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv)
        pc.addTrack(audioTrack)
        pc.add_OnAudioFormatsNegotiated(fun afs -> 
            Utils.debug $"audio format is {Seq.head afs}"
            winAudioEP.SetAudioSinkFormat(Seq.head afs) |> ignore
            winAudioEP.SetAudioSourceFormat(Seq.head afs) |> ignore
        )

        pc.add_OnTimeout(fun mediaType -> Log.info $"Timeout on media {mediaType}")

        pc.add_oniceconnectionstatechange(fun state -> 
            task {
                Utils.debug $"ICE connection state changed to {state}"
                Log.info $"ICE connection state changed to {state}"       
            }
            |> ignore
        )

        pc.add_onconnectionstatechange(fun state -> 
            task {
                if state = RTCPeerConnectionState.connected 
                

            }
            |> ignore
        )

        pc.add_OnRtpPacketReceived(fun ep media packet -> 
            if media = SDPMediaTypesEnum.audio then 
                let ph = packet.Header
                codec.DecodeAndPlay(packet.Payload)                
            )
        audioTrack,codec

    let createDataChannel (pc:RTCPeerConnection) =        
        try
            pc.createDataChannel(C.OPENAI_RT_DATA_CHANNEL).Result |> Some
        with ex -> 
            Log.exn (ex,"createDataChannel")
            None

    let createMediaSenders codec (pc:RTCPeerConnection)   = 
        let audioTrack = createAudioTrack codec pc
        let dataChannel = createDataChannel pc
        audioTrack,dataChannel


type WebRtcClientWin() = 
        
    let mutable peerConnection: RTCPeerConnection = null   
    let mutable dataChannel: RTCDataChannel = Unchecked.defaultof<_>
    let mutable outputChannel = Channels.Channel.CreateBounded<JsonDocument>(30)
    let mutable disposables : IDisposable list  = []
    let mutable state = Disconnected
    let stateEvent = Event<State>()

    let addDisposables xs = disposables <- disposables @ xs
    
    let setState s = state <- s; stateEvent.Trigger(s)

    let onMessage dataChannel payload (bytes:byte[]) = 
        let msg = JsonSerializer.Deserialize(bytes)
        outputChannel.Writer.TryWrite(msg) |> ignore

    member this.Init (codec:Opus.Maui.Graph) () =               
        let pcConfig = RTCConfiguration(X_UseRtpFeedbackProfile = true)
        peerConnection <- Connect.createPeerConnection(pcConfig)
        let audioTrack,dataChannelOpt = Connect.createMediaSenders codec peerConnection
        peerConnection.add_OnTimeout(fun mediaType -> Log.info $"Timeout on media {mediaType}")
        match dataChannelOpt with 
        | Some dc ->
            dataChannel <- dc
            dc.add_onmessage(OnDataChannelMessageDelegate(onMessage))
        | None -> ()

    member this.SendOffer(ephemeralKey:string,url:string) =
        task {
            let offer = peerConnection.createOffer()
            Utils.debug $"Offer SDP:\r\n{offer.sdp}"
            do! peerConnection.setLocalDescription(offer)
            let! answer = Utils.getAnswerSdp ephemeralKey url offer.sdp
            Utils.debug $"Answer SDP:\r\n{answer}"
            let r = peerConnection.setRemoteDescription(RTCSessionDescriptionInit(
                                                        sdp=answer, ``type`` = RTCSdpType.answer))
            if r = SetDescriptionResultEnum.OK then 
                task {setState Connected} |> ignore
            else
                task {setState Disconnected} |> ignore
                Utils.logAndFail "timeout on setting remote sdp as answer"
        }   

    interface System.IDisposable with 
        member this.Dispose (): unit = 
            match peerConnection with 
            | null -> () 
            | x -> 
                x.Close("done")
                x.Dispose()
                peerConnection <- null

            if dataChannel <> Unchecked.defaultof<_> then 
                dataChannel.close()                
                dataChannel <- Unchecked.defaultof<_>
            if outputChannel <>  Unchecked.defaultof<_> then
                outputChannel.Writer.Complete()
                outputChannel <- Unchecked.defaultof<_>
            setState Disconnected            

    //Main cross-platform API
    interface IWebRtcClient with     
        member _.OutputChannel with get () = outputChannel
        member _.State with get() = state
        member _.StateChanged = stateEvent.Publish
                    
        member this.Connect (key,url,codec) = 
            task {
                try
                    let codec:Opus.Maui.Graph = match codec with Some c -> c :?> _ | None -> failwith "codec graph not given"
                    do! MainThread.InvokeOnMainThreadAsync(this.Init codec)                
                    setState Connecting
                    do!this.SendOffer(key,url)
                with ex ->
                    Log.exn (ex,"IWebRtcClient.Connect")
            }

        member this.Send (data:string) =
            if dataChannel <> null then 
                let dataBytes : byte [] = Text.UTF8Encoding.Default.GetBytes data
                dataChannel.send(dataBytes)
                true
            else
                false
#endif
