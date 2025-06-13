namespace FsOpCore

type OTaskTarget = OProcess of string*string option | OLink of string

type OTask = {
    target : OTaskTarget
    cua : string
    reasoner : string option
    voice : string option
}

type OTaskTransition = {
    transition : string option
    task : OTask
}

type OPlan = {
    memory : string list
    tasks : OTaskTransition list
}

type OTaskRun = {
    task   : OTask
    driver : IUIDriver
    messages : ChatMsg list    
}


