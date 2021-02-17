module Modal

open Sutil
open Sutil.DOM
open Sutil.Attr
open Sutil.Bindings
open Sutil.Styling

let view() =
    Html.div [
        let active = Store.make false

        disposeOnUnmount [active]

        Html.button [
            text "Launch example modal"
            onClick (fun _ -> active <~ true) []
        ]

        Html.body [
            Html.div [
                class' "modal"
                bindClass active "is-active"

                Html.div [ class' "modal-background" ]
                Html.div [
                    class' "modal-content"
                    Html.div [
                        class' "box"
                        text "All Your Modal Content Goes Here"
                    ]
                ]
                Html.button [
                    class' "modal-close"
                    onClick (fun _ -> active <~ false) []
                    ariaLabel "close"
                ]
            ]
        ] |> withStyle []
    ]