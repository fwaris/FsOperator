namespace TMOnboad.Server
open TMOnboad.Client
open TMOnboard.Flow
open FsOpCore
open System.Threading.Channels
open FSharp.Control

type FlowRunner(dispatch: ServerInitiatedMessages -> Async<unit>) =

    member this.DispatchMessage(planChannel: Channel<TaskFlowMsgOut>) =
        async {
            let dispatch msg = Async.Start (dispatch msg)
            let channel = planChannel
            channel.Reader.ReadAllAsync()
            |> AsyncSeq.ofAsyncEnum
            |> AsyncSeq.iterAsync (fun msg -> async {
                match msg with
                | APo_Error (WErrorType.WE_Error e) -> dispatch (Srv_Notification e)
                | APo_Error (WErrorType.WE_Exn ex) -> dispatch (Srv_Notification ex.Message)
                | APo_Action act -> dispatch (Srv_Action act)
                | APo_Screenshot scrn -> dispatch (Srv_ScreenShot scrn)
                | APo_GetCredentials -> dispatch Srv_SendCreds
                | APo_GetCode -> dispatch Srv_SendCode
                | APo_Usage usages -> 
                    let usgs = OPlan.sumUsages usages 
                    let tokens = usages |> Map.toSeq |> Seq.sumBy(fun (a,b) ->  b|> Seq.sumBy (fun u->u.input_tokens + u.output_tokens))
                    let cost = usgs |> Map.toSeq |>  Seq.map ModelPricing.calcPrice |> Seq.sum
                    dispatch (Srv_Usage (tokens,cost))
                | APo_Done ts -> dispatch Srv_DonePlan
                | _ -> () //ignore messages for other agents
            })
            |> Async.RunSynchronously
        }
        |> Async.Start

    member this.StartFlow() =
        async {
            let plan, kernel = PortPinV.createWithKernel()
            let runner = OPlanRun.Create plan kernel
            let planChannel = Channel.CreateUnbounded<TaskFlowMsgOut>()
            
            let! result = PlanRunner.run planChannel runner
            return result
        }

module FlowRunner =
    let app : Ref<FlowRunner option> = ref None



