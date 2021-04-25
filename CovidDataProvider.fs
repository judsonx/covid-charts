namespace CovidWeb

module CovidDataProvider =
  open FSharp.Data

  [<Literal>]
  let private statesPath = "DataTemplates/us-states.csv"

  [<Literal>]
  let private usPath = "DataTemplates/us.csv"

  type CovidByState = CsvProvider<statesPath>
  type CovidUs = CsvProvider<usPath>

  let loadByState (path: string) = CovidByState.Load(path)
  let loadUs (path: string) = CovidUs.Load(path)

  let allStates (data: CovidByState) =
    data.Rows
    |> Seq.groupBy (fun r -> r.State)
    |> Seq.map (fun g -> fst g)
    |> Seq.sortBy (fun state -> state)

  let byState (data: CovidByState) state =
    data.Rows |> Seq.filter (fun x -> x.State = state)

  let entireUs (data: CovidUs) =
    data.Rows