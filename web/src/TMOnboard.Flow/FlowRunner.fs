namespace TMOnboard.Flow

module FlowRunner =
    open FsOpCore
    let mutable private flows = Map<string,IFlow<TaskFlowMsgIn>>


    let startFlow (id: string) = ()
        