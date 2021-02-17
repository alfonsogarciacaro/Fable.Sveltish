module Todos

// Adapted from
// https://svelte.dev/examples

open Sutil
open Sutil.Styling
open Sutil.Attr
open Sutil.DOM
open Sutil.Bindings
open Browser.Types

open Sutil.Transition

type Todo = {
        Id : int
        Done: bool
        Description: string
    }

// Todo helpers
let isDone t = t.Done
let isPending = isDone >> not
let key r = r.Id
let hasKey k t = t.Id = k
let description t = t.Description

type Model = {
    Todos : List<Todo>
    Sort : bool
} with
    override this.ToString() = $"The Model: {this.Todos.Length} items"

type Message =
    |AddTodo of desc:string
    |ToggleTodo of id:int
    |DeleteTodo of id:int
    |SetSort of bool
    |CompleteAll

let makeExampleTodos() = [
    { Id = 1; Done = false; Description = "write some docs" }
    { Id = 2; Done = false; Description = "start writing JSConf talk" }
    { Id = 3; Done =  true; Description = "buy some milk" }
    { Id = 4; Done = false; Description = "mow the lawn" }
    { Id = 5; Done = false; Description = "feed the turtle" }
    { Id = 6; Done = false; Description = "fix some bugs" }
]

let newUid = Helpers.makeIdGeneratorFrom(7)

let styleSheet = [

    rule ".new-todo" [
        Css.fontSize (Em 1.4)
        Css.width "100%"
        //margin "2em 0 1em 0"
    ]

    rule ".board" [
        Css.maxWidth "36em"
        Css.margin(Zero,Auto)
    ]

    rule ".todo, .done" [
        //float' "left"
        Css.width "50%"
        Css.padding "0 1em 0 0"
        Css.boxSizing "border-box"
    ]

    rule ".title" [
        Css.marginTop "24px"
    ]

    rule "h2" [
        Css.fontSize "2em"
        Css.fontWeight  "200"
        Css.userSelect  "none"
    ]

    rule "label"  [
        Css.top "0"
        Css.left "0"
        Css.display "block"
        Css.fontSize "1em"
        Css.lineHeight "1"
        Css.padding "0.5em"
        Css.margin(Zero, Auto, Em 0.5, Auto)
        Css.borderRadius "2px"
        Css.backgroundColor "#eee"
        Css.userSelect "none"
    ]

    rule "input" [  Css.margin(0) ]

    rule ".done label" [
        Css.backgroundColor "rgb(180,240,100)"
    ]

    rule "label>button" [
        Css.float' "right"
        Css.height "1em"
        Css.boxSizing "border-box"
        Css.padding "0 0.5em"
        Css.lineHeight "1"
        Css.backgroundColor "transparent"
        Css.border "none"
        Css.color "rgb(170,30,30)"
        Css.opacity "0"
        Css.transition "opacity 0.2s"
    ]

    rule "label:hover button" [
        Css.opacity "1"
    ]

    rule ".row" [
        Css.display "flex"
    ]

    rule ".kudos" [
        Css.fontSize "80%"
        Css.color "#888"
    ]

    rule "div.complete-all-container" [
        Css.display "flex"
        Css.justifyContent "space-between"
        Css.marginTop "4px"
    ]

    rule ".complete-all-container a" [
        Css.cursor "pointer"
        Css.textDecoration "none"

        Css.fontSize "80%"
        Css.color "#888"
    ]

    rule ".complete-all-container a:hover" [
        Css.textDecoration "underline"
    ]
]

let init() = { Todos = makeExampleTodos(); Sort = false }

let toggle id todo = if todo.Id = id then { todo with Done = not todo.Done } else todo

let update (message : Message) (model : Model) : Model =
    match message with
    | SetSort f -> { model with Sort = f }
    | AddTodo desc ->
        let todo = {
            Id = newUid() + 10
            Done = false
            Description = desc
        }
        { model with Todos = model.Todos @ [ todo ] }
    | ToggleTodo id ->
        { model with Todos = model.Todos |> List.map (toggle id) }
    | DeleteTodo id ->
        { model with Todos = model.Todos |> List.filter (fun t -> t.Id <> id) }
    | CompleteAll ->
        { model with Todos = model.Todos |> List.map (fun t -> { t with Done = true }) }


let fader  x = transition <| [ InOut (fade  |> withProps [ Duration 300.0 ])] <| x
let slider x = transition <| [ InOut (slide |> withProps [ Duration 300.0 ])]  <| x

let todosList title (filter : Todo -> bool) tin tout model dispatch =
    let filteredTodos = model |> Store.map (fun m ->
        if m.Sort then
            m.Todos |> List.filter filter |> List.sortBy description
        else
            m.Todos |> List.filter filter
    )
    Html.div [
        class' title
        Html.h2 [ text title ]

        eachk filteredTodos (fun todo ->
            Html.label [
                Html.input [
                    type' "checkbox"
                    attrNotify "checked" todo.Done (fun _ -> todo.Id |> ToggleTodo |> dispatch)
                    ]
                text $" {todo.Description}"
                Html.button [
                    onClick (fun _ -> todo.Id |> DeleteTodo |> dispatch) []
                    text "x"
                ]
            ]
        ) key [In tin; Out tout]
    ]

let makeStore arg = Store.makeElmishSimple init update ignore arg

let fallback (props : TransitionProp list) (node : HTMLElement) = fun _ ->
    let transform = computedStyleTransform node

    { (applyProps props Transition.Default) with
            Duration = 600.0
            Ease = Easing.quintOut
            CssGen = Some(fun t _ -> $"transform: {transform} scale({t}); opacity: {t}") }

let view () : NodeFactory =
    let (send,recv) = crossfade [ Fallback fallback ]

    let model, dispatch = makeStore ()

    let completed = model |> Store.map (fun m -> m.Todos |> List.filter isDone)
    let lotsDone  = completed |> Store.map (fun x -> (x |> List.length >= 3))

    withStyle styleSheet <| Html.div [
        class' "board"

        disposeOnUnmount [ model ]

        Html.input [
            class' "new-todo"
            placeholder "what needs to be done?"
            onKeyDown (fun e ->
                // This isn't the right test for mobile users
                if e.key = "Enter" then (e.currentTarget :?> HTMLInputElement).value |> AddTodo |> dispatch
                printfn($"{e.key}")
            ) []
        ]

        Html.div [
            class' "complete-all-container"
            Bind.fragment model <| fun m -> Html.a [
                href "#"
                text "toggle sort"
                onClick (fun _ -> not m.Sort |> SetSort |> dispatch) [ PreventDefault ]
            ]
            Html.a [
                href "#"
                text "complete all"
                onClick (fun _ -> dispatch CompleteAll) [ PreventDefault ]
            ]
            Html.span [
                class' "kudos"
                Bind.fragment completed (fun x -> text $"{x.Length} tasks completed! Good job!")
            ] |> fader lotsDone
        ]

        Html.div [
            class' "row"
            todosList "todo" isPending recv send model dispatch
            todosList "done" isDone recv send model dispatch
        ]
    ]