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

// ---------------------------------
// Data Provider
// ---------------------------------

module DataProvider =
  open FSharp.Data

  [<Literal>]
  let private statesPath = "/users/judson/fsharp/covid-19-data/us-states.csv"

  [<Literal>]
  let private usPath = "/users/judson/fsharp/covid-19-data/us.csv"

  type CovidByState = CsvProvider<statesPath>
  type CovidUs = CsvProvider<usPath>

  let stateData () = CovidByState.Load(statesPath)
  let usData () = CovidUs.Load(usPath)

  let allStates () =
    (stateData ()).Rows
    |> Seq.groupBy (fun r -> r.State)
    |> Seq.map (fun g -> fst g)
    |> Seq.sortBy (fun state -> state)

  let byState state = (stateData ()).Rows |> Seq.filter (fun x -> x.State = state)
  let entireUs () = (usData ()).Rows

// ---------------------------------
// Views
// ---------------------------------

module Views =
  open Giraffe.ViewEngine

  let stateLinks states =
    let buildLink state = a [ _href $"/states/{state}" ] [ encodedText state ]
    ul [] [ for state in states do li [] [ (buildLink state) ] ]

  let usLink () = ul [] [ li [] [ a [ _href "/" ] [ encodedText "United States" ] ] ]
  let stateLinksNav = nav [ _id "nav" ] [ usLink (); stateLinks (DataProvider.allStates ()) ]

  [<Literal>]
  let url = "https://github.com/nytimes/covid-19-data"

  let dataAttrib =
    [ p [ _class "attrib" ] [ encodedText "Data from "; a [ _href url ] [  encodedText url ] ] ]

  let layout (sectionTitle: string) (navigation: XmlNode) (content: XmlNode list) =
    html [] [
      head [] [
        title [] [ encodedText "US Covid Data" ]
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
    let layout = Layout(title = title, xaxis = Xaxis(title = xTitle), yaxis = Yaxis(title = yTitle), font = Font (family = "Bai Jamjuree"))
    let chart = buildChart mappedData |> Chart.WithLayout layout
    chart.GetInlineHtml() |> Text

  let buildDeathsChartHtml (data: seq<DataProvider.CovidByState.Row>) state =
    buildChartHtml data (fun x -> x.Date, x.Deaths) state "Date" "Deaths"

  let buildCasesChartHtml (data: seq<DataProvider.CovidByState.Row>) state =
    buildChartHtml data (fun x -> x.Date, x.Cases) state "Date" "Cases"

  let buildUsDeathsChartHtml (data: seq<DataProvider.CovidUs.Row>) =
    buildChartHtml data (fun x -> x.Date, x.Deaths) "United States" "Date" "Deaths"

  let buildUsCasesChartHtml (data: seq<DataProvider.CovidUs.Row>) =
    buildChartHtml data (fun x -> x.Date, x.Cases) "United States" "Date" "Cases"

  let formatTotal (total: int) = total.ToString("N0")

  let usChartView usData =
    let last = usData |> Seq.last
    [
      buildUsDeathsChartHtml usData
      p [ _class "total" ] [ encodedText $"Total deaths: {formatTotal last.Deaths}" ]
      buildUsCasesChartHtml usData
      p [ _class "total" ] [ encodedText $"Total cases: {formatTotal last.Cases}" ]
    ] |> layout $"Covid Cases in the United States" stateLinksNav

  let stateChartView state stateData =
    let last = stateData |> Seq.last
    [
      buildDeathsChartHtml stateData state
      p [ _class "total" ] [ encodedText $"Total deaths: {formatTotal last.Deaths}" ]
      buildCasesChartHtml stateData state
      p [ _class "total" ] [ encodedText $"Total cases: {formatTotal last.Cases}" ]
    ] |> layout $"Covid Cases in {state}" stateLinksNav

// ---------------------------------
// Web app
// ---------------------------------

let webApp =
  choose [
    GET >=>
      choose [
        route "/" >=> (htmlView (Views.usChartView (DataProvider.entireUs ())))
        routef "/states/%s" (fun state -> htmlView (Views.stateChartView state (DataProvider.byState state)))
      ]
      setStatusCode 404 >=> text "Not Found"
  ]

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