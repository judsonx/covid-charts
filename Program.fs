module covidweb.App

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Giraffe
open XPlot.Plotly
open CovidWeb

// ---------------------------------
// Data Provider
// ---------------------------------

module DataProvider =
  let private stateData =
    CovidDataProvider.loadByState "/users/judson/fsharp/covid-19-data/us-states.csv"
  let private usData =
    CovidDataProvider.loadUs "/users/judson/fsharp/covid-19-data/us.csv"

  let allStates = CovidDataProvider.allStates stateData
  let byState = CovidDataProvider.byState stateData
  let entireUs = CovidDataProvider.entireUs usData

// ---------------------------------
// Views
// ---------------------------------

module Views =
  open Giraffe.ViewEngine

  let stateLinks states =
    let buildLink state = a [ _href $"/deaths/{state}" ] [ encodedText state ]
    let buildItem content = li [] [ content ]

    ul [] [ for state in states do buildLink state |> buildItem ]

  let usLinks () = ul [] [ a [ _href "/deaths" ] [ encodedText "United States" ] ]
  let stateLinksNav = nav [ _id "nav" ] [ usLinks (); stateLinks DataProvider.allStates ]

  [<Literal>]
  let url = "https://github.com/nytimes/covid-19-data"

  let dataAttrib =
    [ p [ _class "attrib" ] [ encodedText "Data from "; a [ _href url ] [  encodedText url ] ] ]

  let layout (sectionTitle: string) (navigation: XmlNode) (content: XmlNode list) =
    html [] [
      head [] [
        title []  [ encodedText "US Covid Data" ]
        link [ _rel  "stylesheet"; _type "text/css"; _href "/main.css" ]
        link [ _rel "preconnect"; _href "https://fonts.gstatic.com" ]
        link [ _href "https://fonts.googleapis.com/css2?family=Bai+Jamjuree&family=Roboto&display=swap"; _rel "stylesheet" ]
        script [ _src "https://cdn.plot.ly/plotly-latest.min.js" ] []
      ]
      body [] [
        section [] [ h1 [] [ encodedText sectionTitle ] ]
        div [ _id "wrapper" ] [
          navigation
          div [ _id "content" ] (content @ dataAttrib)
        ]
      ]
    ]

  let buildChart data =
    Scatter(
      x = (data |> Seq.map (fun x -> fst x)),
      y = (data |> Seq.map (fun x -> snd x)),
      mode = "lines",
      line = Line(color = "darkslategrey", width = 4.0)
    ) |> Chart.Plot

  let buildChartHtml (data: seq<'a>) (mf: 'a -> DateTime * int) title xTitle yTitle =
    let mappedData = data |> Seq.map mf
    let layout =
      Layout(title = title, xaxis = Xaxis(title = xTitle), yaxis = Yaxis(title = yTitle), font = Font (family = "Bai Jamjuree"))
    let chart = buildChart mappedData |> Chart.WithLayout layout
    chart.GetInlineHtml() |> Text

  let buildDeathsChartHtml (data: seq<CovidDataProvider.CovidByState.Row>) state =
    buildChartHtml data (fun x -> x.Date, x.Deaths) state "Date" "Deaths"

  let buildCasesChartHtml (data: seq<CovidDataProvider.CovidByState.Row>) state =
    buildChartHtml data (fun x -> x.Date, x.Cases) state "Date" "Cases"

  let buildUsDeathsChartHtml (data: seq<CovidDataProvider.CovidUs.Row>) =
    buildChartHtml data (fun x -> x.Date, x.Deaths) "United States" "Date" "Deaths"

  let buildUsCasesChartHtml (data: seq<CovidDataProvider.CovidUs.Row>) =
    buildChartHtml data (fun x -> x.Date, x.Cases) "United States" "Date" "Cases"

  let deathVsCasesNav state =
    div [] [
      a [ _href $"/deaths/{state}" ] [ encodedText "Deaths" ]
      encodedText " | "
      a [ _href $"/cases/{state}" ] [ encodedText "Cases"]
    ]

  let usDeathVsCasesNav () =
    div [] [
      a [ _href $"/deaths" ] [ encodedText "Deaths" ]
      encodedText " | "
      a [ _href $"/cases" ] [ encodedText "Cases"]
    ]

  let formatTotal (total: int) = total.ToString("N0")

  let deathsChartView state stateData =
    let last = stateData |> Seq.last
    [
      deathVsCasesNav state
      buildDeathsChartHtml stateData state
      p [ _class "total" ] [ encodedText $"Total deaths: {formatTotal last.Deaths}" ]
    ] |> layout $"Covid Deaths in {state}" stateLinksNav

  let casesChartView state stateData =
    let last = stateData |> Seq.last
    [
      deathVsCasesNav state
      buildCasesChartHtml stateData state
      p [ _class "total" ] [ encodedText $"Total cases: {formatTotal last.Cases}" ]
    ] |> layout $"Covid Cases in {state}" stateLinksNav

  let usDeathsChartView usData =
    let last = usData |> Seq.last
    [
      usDeathVsCasesNav ()
      buildUsDeathsChartHtml usData
      p [ _class "total" ] [ encodedText $"Total deaths: {formatTotal last.Deaths}" ]
    ] |> layout $"Covid Deaths in the United States" stateLinksNav

  let usCasesChartView usData =
    let last = usData |> Seq.last
    [
      usDeathVsCasesNav ()
      buildUsCasesChartHtml usData
      p [ _class "total" ] [ encodedText $"Total cases: {formatTotal last.Cases}" ]
    ] |> layout $"Covid Cases in the United States" stateLinksNav

// ---------------------------------
// Web app
// ---------------------------------

let deathsByStateHandler state =
  let stateData = DataProvider.byState state
  htmlView (Views.deathsChartView state stateData)

let casesByStateHandler state =
  let stateData = DataProvider.byState state
  htmlView (Views.casesChartView state stateData)

let deathsHandler () = htmlView (Views.usDeathsChartView DataProvider.entireUs)
let casesHandler () = htmlView (Views.usCasesChartView DataProvider.entireUs)

let webApp =
  choose [
    GET >=>
      choose [
        route "/" >=> deathsHandler ()
        route "/deaths" >=> deathsHandler ()
        route "/cases" >=> casesHandler ()
        routef "/deaths/%s" deathsByStateHandler
        routef "/cases/%s" casesByStateHandler
      ]
      setStatusCode 404 >=> text "Not Found"
  ]

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex : Exception) (logger : ILogger) =
  logger.LogError(ex, "An unhandled exception has occurred while executing the request.")
  clearResponse >=> setStatusCode 500 >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder : CorsPolicyBuilder) =
  builder
    .WithOrigins("http://localhost:5000", "https://localhost:5001")
    .AllowAnyMethod()
    .AllowAnyHeader()
  |> ignore

let configureApp (app : IApplicationBuilder) =
  let env = app.ApplicationServices.GetService<IWebHostEnvironment>()
  (match env.IsDevelopment() with
  | true -> app.UseDeveloperExceptionPage()
  | false ->
    app .UseGiraffeErrorHandler(errorHandler)
      .UseHttpsRedirection())
      .UseCors(configureCors)
      .UseStaticFiles()
      .UseGiraffe(webApp)

let configureServices (services : IServiceCollection) =
  services.AddCors() |> ignore
  services.AddGiraffe() |> ignore

let configureLogging (builder : ILoggingBuilder) = builder.AddConsole().AddDebug() |> ignore

[<EntryPoint>]
let main args =
  let contentRoot = Directory.GetCurrentDirectory()
  let webRoot = Path.Combine(contentRoot, "WebRoot")

  Host.CreateDefaultBuilder(args)
    .ConfigureWebHostDefaults(
      fun webHostBuilder ->
        webHostBuilder
          .UseContentRoot(contentRoot)
          .UseWebRoot(webRoot)
          .Configure(Action<IApplicationBuilder> configureApp)
          .ConfigureServices(configureServices)
          .ConfigureLogging(configureLogging)
        |> ignore
    )
    .Build()
    .Run()
  0