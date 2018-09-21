namespace PeopleClient

open WebSharper
open WebSharper.JavaScript
open WebSharper.Mvu
open WebSharper.Sitelets
open PeopleApi.App.Model
open PeopleClient

[<NamedUnionCases>]
type PersonUpdateMessage =
    | SetFirstName of firstName: string
    | SetLastName of lastName: string
    | SetBorn of born: string
    | SetDied of died: string
    | SetHasDied of hasDied: bool

[<NamedUnionCases "type">]
type Message =
    | Goto of page: Page
    | UpdateEditing of msg: PersonUpdateMessage
    | RefreshList of gotoList: bool
    | SubmitCreatePerson
    | SubmitEditPerson of id: PersonId
    | RequestDeletePerson of id: PersonId
    | ConfirmDeletePerson
    | CancelDeletePerson
    | ListRefreshed of people: PersonData[] * gotoList: bool
    | Error of error: string

[<JavaScript>]
module Update =

    let [<Literal>] BaseUrl = "https://peopleapi.websharper.com"

    let route = Router.Infer<EndPoint>()

    let DispatchAjax (endpoint: ApiEndPoint) (parseSuccess: obj -> Message) =
        // We use UpdateModel because SetModel would "overwrite" changes
        // previously done with SetModel.
        UpdateModel (fun state -> { state with Refreshing = true })
        +
        CommandAsync (fun dispatch -> async {
            try
                // Uncomment to delay the command, to see the loading animations
                // do! Async.Sleep 1000
                let! res = Promise.AsAsync <| promise {
                    let! ep =
                        EndPoint.Api (Cors.Of endpoint)
                        |> Router.FetchWith (Some BaseUrl) (RequestOptions()) route
                    return! ep.Json()
                }
                dispatch (parseSuccess res)
            with e ->
                dispatch (Error e.Message)
        })

    let UpdatePerson (message: PersonUpdateMessage) (person: PersonEditing) =
        match message with
        | SetFirstName s -> { person with FirstName = s }
        | SetLastName s -> { person with LastName = s }
        | SetBorn s -> { person with Born = s }
        | SetDied s -> { person with Died = s }
        | SetHasDied b -> { person with HasDied = b }

    let Goto (page: Page) (state: State) : State =
        match page with
        | PeopleList ->
            { state with Page = PeopleList }
        | Creating ->
            { state with
                Page = Creating
                Editing = PersonEditing.Init
            }
        | Editing pid ->
            { state with
                Page = Editing pid
                Editing =
                    Map.tryFind pid state.People
                    |> Option.map PersonEditing.OfData
                    |> Option.defaultValue state.Editing
            }

    let UpdateApp (message: Message) (state: State) : Action<Message, State> =
        match message with
        | Goto page ->
            UpdateModel (Goto page)
        | UpdateEditing msg ->
            SetModel { state with Editing = UpdatePerson msg state.Editing }
        | RefreshList gotoList ->
            DispatchAjax ApiEndPoint.GetPeople
                (fun res -> ListRefreshed (Json.Decode res, gotoList))
        | SubmitCreatePerson ->
            match PersonEditing.TryToData 0 state.Editing with
            | None ->
                SetModel { state with Error = Some "Invalid person" }
            | Some editing ->
                DispatchAjax (ApiEndPoint.CreatePerson editing)
                    (fun _ -> RefreshList true)
        | SubmitEditPerson id ->
            match PersonEditing.TryToData id state.Editing with
            | None ->
                SetModel { state with Error = Some "Invalid person" }
            | Some editing ->
                DispatchAjax (ApiEndPoint.EditPerson editing)
                    (fun _ -> RefreshList true)
        | RequestDeletePerson pid ->
            SetModel { state with Deleting = Some pid }
        | CancelDeletePerson ->
            SetModel { state with Deleting = None }
        | ConfirmDeletePerson ->
            match state.Deleting with
            | None ->
                DoNothing
            | Some pid ->
                DispatchAjax (ApiEndPoint.DeletePerson pid)
                    (fun _ -> RefreshList true)
        | ListRefreshed (people, gotoList) ->
            let people = Map [ for p in people -> p.id, p ]
            let page = if gotoList then PeopleList else state.Page
            SetModel {
                state with
                    Page = page
                    Refreshing = false
                    Deleting = None
                    People = people
                    Error = None
                    Editing =
                        match page with
                        | Editing id ->
                            Map.tryFind id people
                            |> Option.map PersonEditing.OfData
                            |> Option.defaultValue state.Editing
                        | _ -> state.Editing
            }
        | Error err ->
            SetModel { state with Refreshing = false; Error = Some err }
