namespace FsOpMCPServer

type Bill = {FileName:string; Data:byte[] }

type Model = {count :int; bills:Bill list }

type ClientMsg = 
    | GotBill of Bill
    | NoOp