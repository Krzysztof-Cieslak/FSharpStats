#r "packages/Octokit/lib/net45/Octokit.dll"
#r "System.Net.Http"

open Octokit
open Octokit.Internal
open System
open System.Threading
open System.Net.Http
open System.Reflection
open System.IO

//-----------------------------------------------
// Stolen from FAKE
//-----------------------------------------------

// wrapper re-implementation of HttpClientAdapter which works around
// known Octokit bug in which user-supplied timeouts are not passed to HttpClient object
// https://github.com/octokit/octokit.net/issues/963
type private HttpClientWithTimeout(timeout : TimeSpan) as this =
    inherit HttpClientAdapter(fun () -> HttpMessageHandlerFactory.CreateDefault())
    let setter = lazy(
        match typeof<HttpClientAdapter>.GetField("_http", BindingFlags.NonPublic ||| BindingFlags.Instance) with
        | null -> ()
        | f ->
            match f.GetValue(this) with
            | :? HttpClient as http -> http.Timeout <- timeout
            | _ -> ())

    interface IHttpClient with
        member __.Send(request : IRequest, ct : CancellationToken) =
            setter.Force()
            match request with :? Request as r -> r.Timeout <- timeout | _ -> ()
            base.Send(request, ct)

let private isRunningOnMono = System.Type.GetType ("Mono.Runtime") <> null

/// A version of 'reraise' that can work inside computation expressions
let private captureAndReraise ex =
    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw()
    Unchecked.defaultof<_>

/// Retry the Octokit action count times
let rec private retry count asyncF =
    // This retry logic causes an exception on Mono:
    // https://github.com/fsharp/fsharp/issues/440
    if isRunningOnMono then
        asyncF
    else
        async {
            try
                return! asyncF
            with ex ->
                return!
                    match (ex, ex.InnerException) with
                    | (:? AggregateException, (:? AuthorizationException as ex)) -> captureAndReraise ex
                    | _ when count > 0 -> retry (count - 1) asyncF
                    | (ex, _) -> captureAndReraise ex
        }

/// Retry the Octokit action count times after input succeed
let private retryWithArg count input asycnF =
    async {
        let! choice = input |> Async.Catch
        match choice with
        | Choice1Of2 input' ->
            return! (asycnF input') |> retry count
        | Choice2Of2 ex ->
            return captureAndReraise ex
    }

let createClient user password =
    async {
        let httpClient = new HttpClientWithTimeout(TimeSpan.FromMinutes 20.)
        let connection = new Connection(new ProductHeaderValue("FAKE"), httpClient)
        let github = new GitHubClient(connection)
        github.Credentials <- Credentials(user, password)
        return github
    }

//-----------------------------------------------
// Custome code
//-----------------------------------------------



let getRepositoriesWithAtLeast5Start (client : GitHubClient) lang   = async {
    let makeRequest page =
        let req = SearchRepositoriesRequest()
        req.Stars <- Range(5, SearchQualifierOperator.GreaterThan)
        req.Language <- Nullable lang
        req.Page <- page
        req |> client.Search.SearchRepo |> Async.AwaitTask

    let! resp = makeRequest 1
    if resp.TotalCount > 100 then
        let pages = float resp.TotalCount / 100. |> Math.Ceiling |> int
        let! pagesResp = [2 .. pages] |> List.map (fun i -> makeRequest i) |> Async.Parallel
        return
            pagesResp
            |> Seq.collect (fun i -> i.Items)
            |> Seq.append resp.Items
            |> Seq.toList
    else
        return
            resp.Items
            |> Seq.toList
}

let getAllCommitsForRepository (client : GitHubClient) (repository : Repository)  = async {
    let! res = client.Repository.Commit.GetAll(repository.Id) |> Async.AwaitTask
    return res |> Seq.toList
}