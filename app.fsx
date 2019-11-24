﻿  
#if BOOTSTRAP
System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
if not (System.IO.File.Exists "paket.exe") then let url = "https://github.com/fsprojects/Paket/releases/download/3.13.3/paket.exe" in use wc = new System.Net.WebClient() in let tmp = System.IO.Path.GetTempFileName() in wc.DownloadFile(url, tmp); System.IO.File.Move(tmp,System.IO.Path.GetFileName url);;
#r "paket.exe"
Paket.Dependencies.Install (System.IO.File.ReadAllText "paket.dependencies")
#endif

#I "packages/Suave/lib/net40"
#r "packages/Suave/lib/net40/Suave.dll"

module App

open Suave
open Suave.Successful 
open Suave.Filters
open Suave.Operators
open System
open System.IO
open ItemRandomizer

type Context =
    {
        Error: bool;
        Info: string;
        Mode: string;
    }

type TestLocation = 
    {
        Location: string;
        Count: int;
    }

type TestItem = 
    {
        Item: string;
        Locations: TestLocation list;
    }

type TestContext = 
    {
        TestItems: TestItem list;
    }

let index =
    DotLiquid.page "index.html" ()

let information = 
    DotLiquid.page "information.html" ()

let contact = 
    DotLiquid.page "contact.html" ()

let testGet =
    DotLiquid.page "test.html" ()
  
let testPost (r:HttpRequest) = 
    let itemLocations = Randomizer.TestRandomize
    let mutable (testItems:TestItem list) = []
    for i in List.filter (fun (j:Types.Item) -> j.Class = Types.ItemClass.Major) ItemRandomizer.Items.Items do
        let mutable (testLocations:TestLocation list) = []
        for l in List.filter (fun (k:Types.Location) -> k.Class = Types.ItemClass.Major) TournamentLocations.AllLocations do
            let name = l.Name
            let count = List.length (List.filter (fun (f:Types.ItemLocation) -> f.Item.Type = i.Type && f.Location.Address = l.Address) itemLocations)
            let newLocation = { Location = name; Count = count }
            testLocations <- (newLocation :: testLocations)
        let newTestItem = { Item = i.Name; Locations = testLocations }
        testItems <- newTestItem :: testItems
    DotLiquid.page "test.html" { TestItems = testItems }

let randomizeGet =
    DotLiquid.page "randomize.html" ()

let randomizerPost (r:HttpRequest) =
    match r.files.Length with
        | 0 -> DotLiquid.page "randomize.html" { Error = true; Info = "No file uploaded"; Mode = "POST" }
        | _ -> 
            let file = r.files.Head
            let (_,  inputSeed) = r.multiPartFields.Head
            let (_,  inputDifficulty) = r.multiPartFields.Tail.Head;
            let bytes = 
                use binaryReader = new BinaryReader(File.Open(file.tempFilePath, FileMode.Open))
                binaryReader.ReadBytes(int binaryReader.BaseStream.Length)
            try
                let difficulty = enum<Types.Difficulty>(Int32.Parse inputDifficulty)
                let parsedSeed = if inputSeed.StartsWith("0x") then Convert.ToInt32(inputSeed, 16) else Convert.ToInt32(inputSeed)
                let ipsPatches = List.filter (fun (p:Types.IpsPatch) -> ((p.Difficulty = difficulty || p.Difficulty = Types.Difficulty.Any) && p.Default)) Patches.IpsPatches
                let patches = List.filter (fun (p:Types.Patch) -> ((p.Difficulty = difficulty || p.Difficulty = Types.Difficulty.Any) && p.Default)) Patches.RomPatches
                let (seed, binaryData) = Randomizer.Randomize parsedSeed difficulty true "" bytes ipsPatches patches
                let newFileName = sprintf "Item Randomizer %s%d.sfc" (match difficulty with
                                                                        | Types.Difficulty.Casual -> "CX"
                                                                        | Types.Difficulty.Normal -> "X"
                                                                        | Types.Difficulty.Hard -> "HX"
                                                                        | Types.Difficulty.Tournament -> "TX"
                                                                        | Types.Difficulty.Open -> "OX"
                                                                        | Types.Difficulty.Full -> "FX"
                                                                        | _ -> "X") seed
                                                                                        
                if File.Exists(file.tempFilePath) then File.Delete(file.tempFilePath) else ()
                Writers.setMimeType("application/octet-stream")
                >=> Writers.setHeader "Content-Disposition" (sprintf "attachment; filename=\"%s\"" newFileName) 
                >=> (Successful.ok <| binaryData)
            with
                | _ -> DotLiquid.page "randomize.html" { Error = true; Info = "Couldn't patch the ROM, did you upload the correct file?"; Mode = "POST" }
                
let Router = 
    choose [
        path "/" >=> index
        path "/randomize" >=> choose
            [
                POST >=> request randomizerPost
                GET >=> randomizeGet
            ]
        path "/information" >=> information
        path "/contact" >=> contact
        path "/test" >=> choose
            [
                POST >=> request testPost
                GET >=> testGet
            ]
        GET >=> path "/favicon.ico" >=> Files.browseFile __SOURCE_DIRECTORY__ "favicon.ico"
        GET >=> Files.browse (Path.GetFullPath "./static")
        RequestErrors.NOT_FOUND "Page not found"
    ]

let config = 
    let port = System.Environment.GetEnvironmentVariable("PORT")
    let ip127  = IPAddress.Parse("127.0.0.1")
    let ipZero = IPAddress.Parse("0.0.0.0")

    { defaultConfig with 
        bindings=[ (if port = null then HttpBinding.create HTTP ip127 (uint16 8080)
                    else HttpBinding.create HTTP ipZero (uint16 port)) ] }

startWebServer config Router