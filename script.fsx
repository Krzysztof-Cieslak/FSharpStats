#r "packages/Octokit/lib/net45/Octokit.dll"
#load "octokit.fsx"
#r "packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open System
open System.IO
open Newtonsoft.Json

let auth = File.ReadAllLines("auth.data")
let username, password = auth.[0], auth.[1]

let client =
    Octokit.createClient username password
    |> Async.RunSynchronously

let repositories =
    Octokit.Language.FSharp
    |> Octokit.getRepositoriesWithAtLeast5Start client
    |> Async.RunSynchronously

repositories.Length

///This can be loaded from data.json file linked in readme instead of spamming GitHub API again
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

commits.Length

commits
|> JsonConvert.SerializeObject
|> fun n -> File.WriteAllText ("data.json",n)

let commitsWithoutDuplicate = commits |> Seq.distinctBy (fun c -> c.Sha)

commitsWithoutDuplicate |> Seq.length

let commitsByAuthor =
    commitsWithoutDuplicate
    |> Seq.groupBy (fun c -> if isNull c.Author then "Unknown" else c.Author.Login)
    |> Seq.map (fun (k,v) -> k, v |> Seq.length)
    |> Seq.sortBy (fun (k,v) -> v)
    |> Seq.rev


commitsByAuthor |> Seq.iter (fun (k,v) -> printfn "Author %s, Number of commits: %d" k v)
commitsByAuthor |> Seq.length
commitsByAuthor |> Seq.averageBy(fun (k,v) -> float v)
commitsByAuthor |> Seq.where (fun (k,v) -> v > 100) |> Seq.length