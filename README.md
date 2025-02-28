# FlightDeck

<p align="center">
    <img width="400" height="400" src="https://github.com/houstonhaynes/FlightDeck/blob/main/logo/FlightDeckMainLogoFullColor.svg?raw=true">
</p>

FlightDeck is a **scriptable static web site generator** using a type safe F# DSL to define page layouts. This adaptation was largely inspired by Hugo, but leans heavily on the [Markdig](https://github.com/xoofx/markdig/tree/master) C# library for CommonMark support. It also takes some inspiration from the opinions in the [SAFEr Template](https://github.com/Dzoukr/SAFEr.Template). While FlightDeck starts with minimal out-of-the-box JavaScript dependencies, it can be easily extended to include what you need for your site.

## New Site Elements

- 🌷 DaisyUI Theme Support
- 🌬️ Tailwinds CSS classes
- 💌 FontAwesome Free Icons (CDN)
- 🌔 Dark/Light Theme Switch
- ✨ Other pre-built responsive navigation and related elements

## Working features

* Creating custom data loaders using `.fsx` files, meaning you can use anything you can imagine as a source of data for your site, not only predefined `.md` or `.yml` files
* Creating custom generators using `.fsx` files, meaning you can generate any type of output you want
* Dynamic configuration using `.fsx` file
* "Watch mode" provides a hot reload function as you edit that rebuilds your page whenever you change data, or any script file. View your changes as you edit!

## Project History 

<img align="left" width="192" height="192" src="https://github.com/houstonhaynes/FlightDeck/blob/main/logo/Cieslak_avatar.jpg?raw=true">

FlightDeck is a hard fork of [the Fornax project](https://github.com/ionide/Fornax) - an elegant creation of Krzysztof Cieślak that's part of the Ionide tooling suite - You can support Ionide here: [Open Collective](https://opencollective.com/ionide).

[![open collective backers](https://img.shields.io/opencollective/backers/ionide.svg?color=green)](https://opencollective.com/ionide)
[![open collective sponsors](https://img.shields.io/opencollective/sponsors/ionide.svg?color=purple)](https://opencollective.com/ionide)

[![Open Collective](https://opencollective.com/ionide/donate/button.png?color=blue)](https://opencollective.com/ionide)


## Installation

FlightDeck is released as a global .Net Core tool. You can install it with `dotnet tool install FlightDeck -g`

## CLI Application

The main functionality of FlightDeck comes from CLI applications that lets user scaffold, and generate webpages.

* `FlightDeck new` - scaffolds new blog in current working directory using a really simple template
* `FlightDeck build` - builds webpage, puts output to `_public` folder
* `FlightDeck watch` - starts a small webserver that hosts your generated site, and a background process that recompiles the site whenever any changes are detected. This is the recommended way of working with FlightDeck.
* `FlightDeck clean` - removes the output directory and any temp files
* `FlightDeck version` - prints out the currently-installed version of FlightDeck
* `FlightDeck help` - prints out help

## Getting started

Easiest way to get started with `FlightDeck` is running `FlightDeck new` and then `FlightDeck watch` - this will create a fairly minimal blog site template, start `FlightDeck` in watch mode and then start a webserver. Then you can go to `localhost:8080` in your browser to see the page, and edit the scaffolded files in an editor to make changes. Additionally, you can take a look at the `samples` folder in this repository - it has a couple more `loaders` and `generators` that you may use in your website.

## Website definition

FlightDeck is using normal F# code (F# script files) to define any of its core concepts: `loaders`, `generators` and `config`.

### SiteContents

`SiteContents` is a fairly simple type that provides access to any information available to FlightDeck. The information is provided by using `loaders` and can then be accessed in `generators`.

`SiteContents` has several functions in it's public API:

```fsharp
type A = {a: string}
type B = {b: int; c: int}

let sc = SiteContents()
sc.Add({a = "test"})
sc.Add({a = "test2"})
sc.Add({a = "test3"})

sc.Add({b = 1; c = 3}) //You can add objects of different types, `Add` method is generic.

let as = sc.TryGetValues<A>() //This will return an option of sequence of all added elements for a given type - in this case it will be 3 elements
let b = sc.TryGetValue<B>() //This will return an option of element for given type
```

### Loaders

`Loader` is an F# script responsible for loading external data into generation context. The data typically includes things like content of `.md` files, some global site configuration, etc. But since those are normal F# functions, you can do whatever you need.
Want to load information from local database, or from the internet? Sure, why not. Want to use the [World Bank type provider](https://fsprojects.github.io/FSharp.Data/library/WorldBank.html) to include some of the World Bank statistics? That's also possible - you can use any dependency in `loader`, just as in a normal F# script.

`Loaders` are normal F# functions that takes as an input `SiteContents` and absolute path to the page root, and returns `SiteContents`:

```fsharp
#r "../_lib/FlightDeck.Core.dll"

type Page = {
    title: string
    link: string
}

let loader (projectRoot: string) (siteContent: SiteContents) =
    siteContent.Add({title = "Home"; link = "/"})
    siteContent.Add({title = "About"; link = "/about.html"})
    siteContent.Add({title = "Contact"; link = "/contact.html"})

    siteContent
```

**Important note**: You can (and probably should) define multiple loaders - they will all be executed before site generation, and will propagate information into `SiteContents`

### Generators

`Generator` is an F# script responsible for generating output of the FlightDeck process. This is usually `.html` file, but can be anything else - actually `generator` API just requires to return `string` that will be saved to a file. Generators are, again, plain F# functions that as input takes `SiteContents`, absolute path to the page root, relative path to the file that's currently processed (may be empty for the global generators) and returns `string`:

```fsharp
#r "../_lib/FlightDeck.Core.dll"
#if !FlightDeck
#load "../loaders/postloader.fsx"
#endif

open Html

let generate' (ctx : SiteContents) (_: string) =
    let posts = ctx.TryGetValues<Postloader.Post> () |> Option.defaultValue Seq.empty

    let psts =
        posts
        |> Seq.toList
        |> List.map (fun p -> span [] [!! p.link] )

    html [] [
        div [] psts
    ]

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    generate' ctx page
    |> HtmlElement.ToString
```

**Important note**: You can (and probably should) define multiple generators - they will generate different kinds of pages and/or content, such as `post`, `index`, `about`, `rss` etc.

### Configuration

`Configuration` is an F# script file that defines when which analyzers need to be run, and how to save its output. A `Config.fsx` file needs to be put in the root of your site project (the place from which you run the `FlightDeck` CLI tool)

```fsharp
#r "../_lib/FlightDeck.Core.dll"

open Config
open System.IO

let postPredicate (projectRoot: string, page: string) =
    let fileName = Path.Combine(projectRoot,page)
    let ext = Path.GetExtension page
    if ext = ".md" then
        let ctn = File.ReadAllText fileName
        ctn.Contains("layout: post")
    else
        false

let staticPredicate (projectRoot: string, page: string) =
    let ext = Path.GetExtension page
    if page.Contains "_public" ||
       page.Contains "_bin" ||
       page.Contains "_lib" ||
       page.Contains "_data" ||
       page.Contains "_settings" ||
       page.Contains "_config.yml" ||
       page.Contains ".sass-cache" ||
       page.Contains ".git" ||
       page.Contains ".ionide" ||
       ext = ".fsx"
    then
        false
    else
        true

let config = {
    Generators = [
        {Script = "less.fsx"; Trigger = OnFileExt ".less"; OutputFile = ChangeExtension "css" }
        {Script = "sass.fsx"; Trigger = OnFileExt ".scss"; OutputFile = ChangeExtension "css" }
        {Script = "post.fsx"; Trigger = OnFilePredicate postPredicate; OutputFile = ChangeExtension "html" }
        {Script = "staticfile.fsx"; Trigger = OnFilePredicate staticPredicate; OutputFile = SameFileName }
        {Script = "index.fsx"; Trigger = Once; OutputFile = NewFileName "index.html" }

    ]
}

```

Possible Generator Triggers are:
- `Once` : Runs once, globally.
- `OnFile filename`: Run once for the given filename.
- `OnFileExt extension` : Runs once for each file with the given extension.
- `OnFilePredicate predicate` : Runs once for each file satisfying the predicate (`string -> string`).

Possible Generator Outputs are:
- `SameFileName` :  Output has the same filename as input file.
- `ChangeExtension newExtension` : Output has the same filename but with extention change to `newExtension`.
- `NewFileName newFileName` : Output filename is `newFileName`.
- `Custom mapper` : Output filename is the result of applying the mapper to the input filename.
- `MultipleFiles mapper` : Outputs multiple files, the names of which are a result of applying the mapper to the first string output of the generator.

**Note**: For `MultipleFiles` the `generate` function *must* output a `list<string * string>`.


## How to contribute

*Imposter syndrome disclaimer*: I want your help. No really, I do.

There might be a little voice inside that tells you you're not ready; that you need to do one more tutorial, or learn another framework, or write a few more blog posts before you can help me with this project.

I assure you, that's not the case.

This project has some clear Contribution Guidelines and expectations that you can [read here](https://github.com/Ionide/FlightDeck/blob/master/CONTRIBUTING.md).

The contribution guidelines outline the process that you'll need to follow to get a patch merged. By making expectations and process explicit, I hope it will make it easier for you to contribute.

And you don't just have to write code. You can help out by writing documentation, tests, or even by giving feedback about this work. (And yes, that includes giving feedback about the contribution guidelines.)

Thank you for contributing!

## Build process

 * You need [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
 * Run `dotnet tool restore` to restore the .NET 6 local tools defined at .config/dotnet-tools.json
 * To build the project run `dotnet run` (this will run the `build.fsproj` project that contains the FAKE build pipeline.)
 * To run unit tests run `dotnet run Test`


## Contributing and copyright

The project is hosted on [GitHub](https://github.com/Ionide/FlightDeck) where you can [report issues](https://github.com/Ionide/FlightDeck/issues), fork
the project and submit pull requests. Please read [Contribution Guide](https://github.com/Ionide/FlightDeck/blob/master/CONTRIBUTING.md)

The library is available under [MIT license](https://github.com/Ionide/FlightDeck/blob/master/LICENSE.md), which allows modification and redistribution for both commercial and non-commercial purposes.

Please note that this project is released with a [Contributor Code of Conduct](CODE_OF_CONDUCT.md). By participating in this project you agree to abide by its terms.

