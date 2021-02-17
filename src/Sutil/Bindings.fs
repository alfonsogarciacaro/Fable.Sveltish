[<AutoOpen>]
module Sutil.Bindings

open Transition
open DOM
open Browser.Types
open Browser.Dom
open System
open Fable.Core

let private log s = Logging.log "bind" s

let private bindId = Helpers.makeIdGenerator()

// All bindings ought to either end up calling this or at least doing the same registration
let bindSub<'T> (source : IObservable<'T>) (handler : BuildContext -> 'T -> unit) = nodeFactory <| fun ctx ->
    let unsub = source.Subscribe( handler ctx )
    registerDisposable ctx.Parent unsub
    unitResult()

let bindFragment<'T>  (store : IObservable<'T>)  (element: 'T -> NodeFactory) = nodeFactory <| fun ctx ->
    let mutable node = null

    let unsub = Store.subscribe store ( fun next ->
        try
            node <- buildSolitary (element(next)) (if isNull node then ctx else ctx |> ContextHelpers.withReplace node)
        with
        | x -> Logging.error $"Exception in bind: {x.Message} parent {nodeStr ctx.Parent} node {nodeStr node} node.Parent "
    )

    registerDisposable ctx.Parent unsub
    bindResult (RealNode(node))

let bindPromiseStore<'T>  (p : ObservablePromise<'T>)
        (waiting : NodeFactory)
        (result: 'T -> NodeFactory)
        (fail : Exception -> NodeFactory)
        : NodeFactory =
    bindFragment p <| (function
        | Waiting -> waiting
        | Result r -> result r
        | Error x -> fail x)

let bindPromise<'T>  (p : JS.Promise<'T>)
        (waiting : NodeFactory)
        (result: 'T -> NodeFactory)
        (fail : Exception -> NodeFactory)
        : NodeFactory =
    let x = ObservablePromise<'T>()
    x.Run p
    bindPromiseStore x waiting result fail

type BindFn<'T> = IObservable<'T> -> ('T -> NodeFactory) -> NodeFactory

let bindFragment2<'A,'B> (a : IObservable<'A>) (b : IObservable<'B>)  (element: ('A*'B) -> NodeFactory) = nodeFactory <| fun ctx ->
    let mutable node = Unchecked.defaultof<_>

    let unsub = Store.subscribe2 a b (fun next ->
        try
            //buildSolitary (element next) { ctx with AppendChild = (makeAppendChild ctx node.Value) }
            node <- buildSolitary (element(next)) (ctx |> ContextHelpers.withReplace node)
        with
        | x -> Logging.error $"Exception in bind: {x.Message}"
    )

    registerDisposable ctx.Parent unsub
    bindResult (RealNode(node))

let private getInputChecked el = Interop.get el "checked"
let private setInputChecked (el : Node) (v:obj) = Interop.set el "checked" v
let private getInputValue el : string = Interop.get el "value"
let private setInputValue el (v:string) = Interop.set el "value" v

let bindSelect<'T when 'T : equality> (store:Store<'T>) : NodeFactory = nodeFactory <| fun ctx ->

    let select = ctx.Parent :?> HTMLSelectElement
    let op (coll:HTMLCollection) i = coll.[i] :?> HTMLOptionElement
    let opValue op : 'T = Interop.get op "__value"

    let getValue() =
        let selOps = select.selectedOptions
        opValue selOps.[0]
        //[0..selOps.length-1] |> List.map (fun i -> opValue (op selOps i))

    let updateSelected (v : 'T) =
        for i in [0..select.options.length-1] do
            let o = select.options.[i] :?> HTMLOptionElement
            o.selected <- (v = (opValue o))

    // Update the store when the radio box is clicked on
    let unsubInput = DOM.listen "input" select <| fun _ ->
        //log($"%A{getValueList()}")
        getValue() |> Store.set store

    // We need to finalize checked status after all attrs have been processed for input,
    // in case 'value' hasn't been set yet
    once Event.ElementReady select <| fun _ ->
        store |> Store.get |> updateSelected

    // When store changes make sure check status is synced
    let unsub = Store.subscribe store updateSelected

    registerUnsubscribe ctx.Parent unsubInput
    registerDisposable ctx.Parent unsub

    unitResult()

let bindSelectMultiple<'T when 'T : equality> (store:Store<List<'T>>) : NodeFactory = nodeFactory <| fun ctx ->

    let select = ctx.Parent :?> HTMLSelectElement
    let op (coll:HTMLCollection) i = coll.[i] :?> HTMLOptionElement
    let opValue op : 'T = Interop.get op "__value"

    let getValueList() =
        let selOps = select.selectedOptions
        [0..selOps.length-1] |> List.map (fun i -> opValue (op selOps i))

    let updateSelected (v : List<'T>) =
        for i in [0..select.options.length-1] do
            let o = select.options.[i] :?> HTMLOptionElement
            o.selected <- v |> List.contains (opValue o)

    // Update the store when the radio box is clicked on
    let unsubInput = DOM.listen "input" select <| fun _ ->
        getValueList() |> Store.set store

    // We need to finalize checked status after all attrs have been processed for input,
    // in case 'value' hasn't been set yet
    once Event.ElementReady select <| fun _ ->
        store |> Store.get |> updateSelected

    // When store changes make sure check status is synced
    let unsub = Store.subscribe store (updateSelected)

    registerDisposable ctx.Parent unsub
    registerUnsubscribe ctx.Parent unsubInput

    unitResult()

let private isNullString (obj:obj) =
    isNull obj || System.String.IsNullOrEmpty(downcast obj)

let private getId (s : IStore<'T>) = s.GetHashCode()

let bindGroup<'T> (store:Store<List<string>>) : NodeFactory = nodeFactory <| fun ctx ->
    let parent = ctx.Parent
    let name = match Interop.get parent "name" with
                | s when isNullString s -> $"store-{getId store}"
                | s -> s

    // Group this input with all other inputs that reference the same store
    Interop.set parent "name" name

    let getValueList() =
        let inputs = (documentOf parent).querySelectorAll(@$"input[name=""{name}""]")
        [0..(inputs.length-1)] |> List.map (fun i -> inputs.[i]) |> List.filter getInputChecked |> List.map getInputValue

    let updateChecked (v : List<string>) =
        setInputChecked parent ( v |> List.contains (getInputValue parent) )

    // Update the store when the radio box is clicked on
    let unsubInput = DOM.listen "input" parent <| fun _ ->
        getValueList() |> Store.set store

    // We need to finalize checked status after all attrs have been processed for input,
    // in case 'value' hasn't been set yet
    once Event.ElementReady parent <| fun _ ->
        store |> Store.get |> updateChecked

    // When store changes make sure check status is synced
    let unsub = Store.subscribe store (updateChecked)

    registerDisposable ctx.Parent unsub
    registerUnsubscribe ctx.Parent unsubInput

    unitResult()

// T can realistically only be numeric or a string. We're relying (I think!) on JS's ability
// to turn a string into an int automatically in the Store.set call (maybe it's Fable doing that)
//
let bindRadioGroup<'T> (store:Store<'T>) : NodeFactory = nodeFactory <| fun ctx ->
    let parent = ctx.Parent
    let name = match Interop.get parent "name" with
                | s when isNullString s -> $"store-{getId store}"
                | s -> s
    // Group this input with all other inputs that reference the same store
    Interop.set parent "name" name

    let updateChecked (v : obj) =
        setInputChecked parent ( (string v) = getInputValue parent )

    // Update the store when the radio box is clicked on
    let inputUnsub = listen "input" parent <| fun _ ->
        Interop.get parent "value" |> Store.set store

    // We need to finalize checked status after all attrs have been processed for input,
    // in case 'value' hasn't been set yet
    once Event.ElementReady parent <| fun _ ->
        store |> Store.get |> updateChecked

    // When store changes make sure check status is synced
    let unsub = Store.subscribe store updateChecked

    registerDisposable ctx.Parent unsub
    registerUnsubscribe ctx.Parent inputUnsub

    unitResult()

let bindClass (toggle:IObservable<bool>) (classes:string) =
    bindSub toggle <| fun ctx active ->
        if active then
            addToClasslist ctx.ParentElement classes
        else
            removeFromClasslist ctx.ParentElement classes

// Bind a store value to an element attribute. Updates to the element are unhandled
let bindAttrIn<'T> (attrName:string) (store : IObservable<'T>) : NodeFactory = nodeFactory <| fun ctx ->
    let unsub = Store.subscribe store (Interop.set ctx.Parent attrName)
    registerDisposable ctx.Parent unsub
    unitResult()

let bindAttrOut<'T> (attrName:string) (onchange : 'T -> unit) : NodeFactory = nodeFactory <| fun ctx ->
    let parent = ctx.Parent
    let unsubInput = listen "input" parent <| fun _ ->
        Interop.get parent attrName |> onchange
    registerUnsubscribe parent unsubInput
    unitResult()

// Bind a scalar value to an element attribute. Listen for onchange events and dispatch the
// attribute's current value to the given function. This form is useful for view templates
// where v is invariant (for example, an each that already filters on the value of v, like Todo.Done)
let attrNotify<'T> (attrName:string) (value :'T) (onchange : 'T -> unit) : NodeFactory = nodeFactory <| fun ctx ->
    let parent = ctx.Parent
    let unsubInput = listen "input" parent  <| fun _ ->
        Interop.get parent attrName |> onchange
    Interop.set parent attrName value
    DOM.registerUnsubscribe ctx.Parent unsubInput
    unitResult()

// Bind an observable value to an element attribute. Listen for onchange events and dispatch the
// attribute's current value to the given function
let bindAttrBoth<'T> (attrName:string) (value : IObservable<'T>) (onchange : 'T -> unit) : NodeFactory =
    bindAttrIn attrName value |> ignore
    bindAttrOut attrName onchange

let bindListen<'T> (attrName:string) (store : IObservable<'T>) (event:string) (handler : Event -> unit) : NodeFactory = nodeFactory <| fun ctx ->
    let parent = ctx.Parent
    let unsubA = Sutil.DOM.listen event parent handler
    let unsubB = Store.subscribe store ( Interop.set parent attrName )
    registerUnsubscribe ctx.Parent unsubA
    registerDisposable ctx.Parent unsubB
    unitResult()

// Bind a store value to an element attribute. Listen for onchange events write the converted
// value back to the store
let private bindAttrConvert<'T> (attrName:string) (store : Store<'T>) (convert : obj -> 'T) : NodeFactory = nodeFactory <| fun ctx ->
    let parent = ctx.Parent
    //let attrName' = if attrName = "value" then "__value" else attrName
    let unsubInput = DOM.listen "input" parent <| fun _ ->
        Interop.get parent attrName |> convert |> Store.set store
    let unsub = Store.subscribe store ( Interop.set parent attrName )
    registerUnsubscribe parent unsubInput
    registerDisposable parent unsub
    unitResult()

// Unsure how to safely convert Element.getAttribute():string to 'T
let private convertObj<'T> (v:obj) : 'T  =
    v :?> 'T

// Bind a store to an attribute in both directions
let bindAttrStoreBoth<'T> (attrName:string) (store : Store<'T>) =
    bindAttrConvert attrName store convertObj<'T>

let bindAttrStoreOut<'T> (attrName:string) (store : Store<'T>) : NodeFactory = nodeFactory <| fun ctx ->
    let parent = ctx.Parent
    let unsubInput = DOM.listen "input" parent <| fun _ ->
        Interop.get parent attrName |> convertObj<'T> |> Store.set store
    //(asEl parent).addEventListener("input", (fun _ -> Interop.get parent attrName |> convertObj<'T> |> Store.set store ))
    registerUnsubscribe parent unsubInput
    unitResult()

let private attrIsSizeRelated  (attrName:string) =
    let upr = attrName.ToUpper()
    upr.IndexOf("WIDTH") >= 0 || upr.IndexOf("HEIGHT") >= 0

let listenToProp<'T> (attrName:string) (dispatch: 'T -> unit) : NodeFactory = nodeFactory <| fun ctx ->
    let parent = ctx.Parent
    let notify() = Interop.get parent attrName |> convertObj<'T> |> dispatch

    once Event.ElementReady parent <| fun _ ->
        if attrIsSizeRelated attrName then
            (ResizeObserver.getResizer (downcast parent)).Subscribe( notify ) |> DOM.registerDisposable parent
        else
            DOM.listen "input" parent (fun _ -> notify()) |> DOM.registerUnsubscribe parent

        rafu notify

    unitResult()

let bindPropOut<'T> (attrName:string) (store : Store<'T>) : NodeFactory =
    listenToProp attrName (Store.set store)

type KeyedStoreItem<'T,'K> = {
    Key : 'K
    Element : HTMLElement
    SvId : int
    Position : IStore<int>
    Value: IStore<'T>
    Rect: ClientRect
}

let private findCurrentNode (current:Node) (id:int) =
    if (isNull current.parentNode) then
        log($"each: Node {nodeStr current} was replaced - finding new one with id {id}")
        match DOM.findNodeWithSvId (documentOf current) id with
        | None ->
            log("each: Disaster: cannot find node")
            null
        | Some n ->
            log($"each: Found it: {n}")
            n
    else
        current

let private findCurrentElement (current:Node) (id:int) =
    let node = findCurrentNode current id
    match node with
    | null -> null
    | n when isElementNode n -> n :?> HTMLElement
    | x ->  log $"each: Disaster: found node but it's not an HTMLElement"
            null

let private genEachId = Helpers.makeIdGenerator()

let eachiko (items:IObservable<list<'T>>) (view : IObservable<int> * IObservable<'T> -> NodeFactory) (key:int*'T->'K) (trans : TransitionAttribute list) : NodeFactory =
    nodeFactory <| fun ctx ->
        let log s = Logging.log "each" s
        let mutable state : KeyedStoreItem<'T,'K> list = []
        let eachId = genEachId()
        let idKey = "svEachId"
        let hasEid (n : Node) = Interop.exists n idKey
        let eachIdOf n : int = if hasEid n then  Interop.get n idKey else -1
        let setEid n = Interop.set n idKey eachId

        let unsub = Store.subscribe items (fun newItems ->
            let wantAnimate = true

            log("-- Each Block Render -------------------------------------")
            log($"caching rects for render. Previous: {state |> List.length} items. Current {newItems |> List.length} items")

            state <- state |> List.map (fun ki ->
                let el = findCurrentElement ki.Element ki.SvId
                { ki with Element = el; Rect = el.getBoundingClientRect() })

            // Last child that doesn't have our eachId
            let prevNodeInit : Node = lastChildWhere ctx.Parent ((<>) eachId << eachIdOf)
            let mutable prevNode = prevNodeInit

            let newState = newItems |> List.mapi (fun itemIndex item ->
                let itemKey = key(itemIndex,item)
                let optKi = state |> Seq.tryFind (fun x -> x.Key = itemKey)
                console.log($"i={itemIndex} k={itemKey}")
                match optKi with
                | None ->
                    let storePos = Store.make itemIndex
                    let storeVal = Store.make item
                    let ctx2 = ctx |> ContextHelpers.withAfter prevNode
                    log $"creating new item after {nodeStr prevNode}"
                    let itemNode = buildSolitaryElement (view (storePos,storeVal)) ctx2
                    setEid itemNode
                    registerDisposable itemNode storePos
                    registerDisposable itemNode storeVal
                    transitionNode itemNode trans [Key (string itemKey)] true ignore ignore
                    let newKi = {
                        SvId = svId itemNode
                        Key = itemKey
                        Element = itemNode
                        Position = storePos
                        Rect = itemNode.getBoundingClientRect()
                        Value = storeVal
                    }
                    log $"new item {newKi.SvId} {itemKey} {rectStr newKi.Rect}"
                    prevNode <- itemNode
                    newKi
                | Some ki ->
                    ki.Position |> Store.modify (fun _ -> itemIndex)
                    ki.Value |> Store.modify (fun _ -> item)
                    log $"existing item {ki.SvId} {ki.Key} {rectStr ki.Rect}"
                    if wantAnimate then
                        clearAnimations ki.Element
                        animateNode ki.Element (ki.Rect)
                    prevNode <- ki.Element
                    ki
            )

            // Remove old items
            for oldItem in state do
                if not (newState |> Seq.exists (fun x -> x.Key = oldItem.Key)) then
                    log($"removing key {oldItem.Key}")
                    fixPosition oldItem.Element
                    ctx.Parent.removeChild(oldItem.Element) |> ignore
                    ctx.Parent.insertBefore(oldItem.Element,null) |> ignore
                    transitionNode oldItem.Element trans [Key (string oldItem.Key)] false
                        ignore //fixPosition
                        (fun e ->
                            oldItem.Position.Dispose()
                            oldItem.Value.Dispose()
                            unmount e)

            // Reorder
            prevNode <- prevNodeInit
            for ki in newState do
                if not(isSameNode prevNode ki.Element.previousSibling) then
                    log($"reordering key {ki.Key}")
                    ctx.Parent.removeChild(ki.Element) |> ignore
                    insertAfter ctx.Parent prevNode ki.Element
                prevNode <- ki.Element

            state <- newState
        )
        DOM.registerDisposable ctx.Parent unsub
        unitResult()

let private duc = ObservableX.distinctUntilChanged

let each (items:IObservable<list<'T>>) (view : 'T -> NodeFactory) (trans : TransitionAttribute list) =
    eachiko items (fun (_,item) -> bindFragment (duc item) view) (fun (_,v) -> v.GetHashCode()) trans

let eachi (items:IObservable<list<'T>>) (view : (int*'T) -> NodeFactory)  (trans : TransitionAttribute list) : NodeFactory =
    eachiko items (fun (index,item) -> bindFragment2 (duc index) (duc item) view) fst trans

let eachio (items:IObservable<list<'T>>) (view : (IObservable<int>*IObservable<'T>) -> NodeFactory)  (trans : TransitionAttribute list) =
    eachiko items view fst trans

let eachk (items:IObservable<list<'T>>) (view : 'T -> NodeFactory)  (key:'T -> 'K) (trans : TransitionAttribute list) =
    eachiko
        items
        (fun (_,item) -> bindFragment (duc item) view)
        (snd>>key)
        trans

//
// Turn events into an IObservable using a map function
// Pass IObservable to NodeFactory function so that containing element can be side-effected (eg, bindClass)
//
let bindEvent<'T> (event:string) (map:Event -> 'T) (app:IObservable<'T> -> DOM.NodeFactory) : DOM.NodeFactory = nodeFactory <| fun ctx ->
    let s = Store.make Unchecked.defaultof<'T>
    let u = listen event ctx.Parent (map >> Store.set s)
    registerDisposable ctx.Parent s
    registerUnsubscribe ctx.Parent u
    ctx |> (s |> app |> build)

let bindEventU<'T> (event:string) (map:Event -> 'T) (app:IObservable<'T> -> unit) : DOM.NodeFactory =
     bindEvent event map (fun s -> app(s); fragment[])

let bindStore<'T> (init:'T) (app:Store<'T> -> DOM.NodeFactory) : DOM.NodeFactory = nodeFactory <| fun ctx ->
    let s = Store.make init
    registerDisposable ctx.Parent s
    ctx |> (s |> app |> build)

let declareStore<'T> (init : 'T) (f : Store<'T> -> unit) =
    declareResource (fun () -> Store.make init) f

let (|=>) a b = bindFragment a b

let selectApp (selectors : (IObservable<bool> * (unit ->NodeFactory)) list) = nodeFactory <| fun ctx ->
    let s = selectors |> List.map fst |> firstOf
    let apps = selectors |> List.map snd |> Array.ofList

    let u = s.Subscribe(fun i ->
        if i >= 0 then
            build (exclusive (apps.[i]())) ctx |> ignore
    )

    unitResult()


// BindApi is a way for me to refactor this module into a public-facing documentation API with
// overloads where appropriate.
// Some examples will still be referencing Bindings.*

[<AutoOpen>]
module BindApi =
    type Bind =
        /// Dual-binding for a given attribute. Changes to value are written to the attribute, while
        /// changes to the attribute are written back to the store. Note that an IStore is also
        /// an IObservable, for which a separate overload exists.
        static member attr<'T> (name:string, value: IStore<'T>) = bindAttrStoreBoth name value

        /// One-way binding from value to attribute. Note that passing store to this function will
        /// select the more specific `attr<'T>( string, IStore<'T>)` overload.
        /// If that looks to be a problem, we'll rename both of them to force a considered choice.
        static member attr<'T> (name:string, value: IObservable<'T>) = bindAttrIn name value

        /// One-way binding from attribute to dispatch function
        static member attr<'T> (name:string, dispatch: 'T -> unit) = bindAttrOut name dispatch

        /// Two-way binding from value to attribute and from attribute to dispatch function
        static member attr<'T> (name:string, value: IObservable<'T>, dispatch: 'T -> unit) = bindAttrBoth name value dispatch

        /// Binding from value to a DOM fragment. Each change in value replaces the current DOM fragment
        /// with a new one.
        static member fragment<'T>  (value : IObservable<'T>)  (element: 'T -> NodeFactory) = bindFragment value element

        /// Binding from two values to a DOM fragment. See fragment<'T>
        static member fragment2<'A,'B>  (valueA : IObservable<'A>) (valueB : IObservable<'B>) (element: 'A * 'B -> NodeFactory) = bindFragment2 valueA valueB element



