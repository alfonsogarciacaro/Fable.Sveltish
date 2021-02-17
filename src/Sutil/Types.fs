namespace Sutil
open System

type IStoreDebugger = interface
    abstract Value: obj
    abstract NumSubscribers : int
    end


module DevToolsControl =

    type SutilOptions = {
        SlowAnimations : bool
        LoggingEnabled : bool
    }

    let mutable Options = {
        SlowAnimations = false
        LoggingEnabled = false
    }

    type Version = {
        Major : int
        Minor : int
        Patch : int
    }
    with override v.ToString() = $"{v.Major}.{v.Minor}.{v.Patch}"

    type IMountPoint = interface
        abstract Id : string
        abstract Remount : unit -> unit
        end

    type IControlBlock = interface
        abstract ControlBlockVersion : int
        abstract Version: Version
        abstract GetOptions : unit -> SutilOptions
        abstract SetOptions : SutilOptions -> unit
        abstract GetStores : unit -> int array
        abstract GetStoreById : int -> IStoreDebugger
        abstract GetLogCategories: unit -> (string * bool) array
        abstract SetLogCategories: (string * bool) array -> unit
        abstract GetMountPoints: unit -> IMountPoint array
        end

    let getControlBlock doc : IControlBlock = Interop.get doc "__sutil_cb"
    let setControlBlock doc (cb : IControlBlock)  = Interop.set doc "__sutil_cb" cb

    let initialise doc controlBlock =
        setControlBlock doc controlBlock

type Unsubscribe = unit -> unit

type IStore<'T> = interface
    inherit IObservable<'T>
    inherit IDisposable
    abstract Update: f:('T -> 'T) -> unit
    abstract Value: 'T
    abstract Debugger : IStoreDebugger
    end

type Store<'T> = IStore<'T>

type Update<'Model> = ('Model -> 'Model) -> unit // A store updater. Store updates by being passed a model updater
type Dispatch<'Msg> = 'Msg -> unit // Message dispatcher
type Cmd<'Msg> = (Dispatch<'Msg> -> unit) list // List of commands. A command needs a dispatcher to execute

module Cmd =

    let none : Cmd<'Msg> = [ ]

    let map (f : 'MsgA -> 'MsgB) (cmd : Cmd<'MsgA>) : Cmd<'MsgB> =
        cmd |> List.map (fun g -> (fun dispatch -> f >> dispatch) >> g)

    let ofMsg msg : Cmd<'Msg> = [ fun d -> d msg ]

    let batch (cmds : Cmd<'Msg> list) : Cmd<'Msg> = cmds |> List.concat

    module OfFunc =
        let either (task: 'args -> _)  (a:'args) (success:_ -> 'msg')(error: _ -> 'msg') =
            [ fun d -> try task a |> (success >> d) with |x -> x |> (error >> d) ]

        let perform (task: 'args -> _) (a:'args) (success:_ -> 'msg') =
            [ fun d -> try task a |> (success >> d) with |_ -> () ]

        let attempt (task: 'args -> unit) (a:'args) (error: _ -> 'msg') =
            [ fun d -> try task a with |x -> x |> (error >> d) ]
