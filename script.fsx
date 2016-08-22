#r "packages/Octokit/lib/net45/Octokit.dll"
#load "octokit.fsx"

open System
open System.IO

let auth = File.ReadAllLines("auth.data")
let username, password = auth.[0], auth.[1]

let client =
    Octokit.createClient username password
    |> Async.RunSynchronously

let repositories =
    Octokit.Language.FSharp
    |> Octokit.getRepositoriesWithAtLeast5Start client
    |> Async.RunSynchronously

//shoudl be 577
repositories.Length

let commits =
    repositories
    |> List.chunkBySize 30
    |> List.mapi (fun i chunk ->
        let r=
            chunk
            |> List.map (Octokit.getAllCommitsForRepository client )
            |> Async.Parallel
            |> Async.RunSynchronously
            |> Seq.collect id
            |> Seq.toList
        printfn "Chunk %d done" i
        System.Threading.Thread.Sleep(TimeSpan.FromMinutes 1.)
        r
    )
    |> List.collect id