namespace FsOperator

type Model = {data:string}
with static member Default = {data="data1"}

type ClientMsg =
    | Initialize
    | Connect

