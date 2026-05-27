namespace ApexRacers.Seeder;

// Car IDs in the 3000-7000 range are synthetic — they will not conflict with
// real iRacing car IDs (which are typically below ~300).
// Series/Season IDs in the 9000/90000 range are similarly synthetic.

public record CarDef(int CarId, string Name, string Abbr, double OffsetSeconds);

// BaseLapSeconds = median time for a midfield driver in the "reference" car class.
// StdDevSeconds  = natural lap-to-lap variation (gaussian noise applied per entry).
// GtpOffsetSeconds / LigierOffsetSeconds = additional offset for the faster class.
public record WeekDef(
    int WeekNumber,
    int TrackId,
    string TrackName,
    string ConfigName,
    DateOnly StartDate,
    double BaseLapSeconds,
    double StdDevSeconds,
    double FasterClassOffset = 0.0);

public record SeriesDef(
    int SeriesId,
    int SeasonId,
    string Name,
    int Year,
    int Quarter,
    List<CarDef> Cars,
    List<WeekDef> Weeks,
    List<CarDef>? FasterClassCars = null);

public static class SeedData
{
    // ── Shared car definitions ────────────────────────────────────────────────

    // GT3 cars — offset from the Porsche 911 GT3 R (992) baseline
    private static readonly List<CarDef> Gt3Cars =
    [
        new(3001, "Mercedes-AMG GT3 2020",        "AMG GT3 2020",    -0.5),
        new(3002, "BMW M4 GT3 EVO",               "M4 GT3 EVO",      -1.2),
        new(3003, "Porsche 911 GT3 R (992)",       "911 GT3 R",        0.0),
        new(3004, "Ford Mustang GT3",              "Mustang GT3",     +0.5),
        new(3005, "Chevrolet Corvette Z06 GT3.R",  "Vette GT3.R",     +1.5),
        new(3006, "Aston Martin Vantage GT3 EVO",  "Vantage GT3 EVO", -2.0),
        new(3007, "Lamborghini Huracán GT3 EVO",   "Huracán GT3 EVO", +1.2),
        new(3008, "Acura NSX GT3 EVO 22",          "NSX GT3 EVO 22",  +2.2),
        new(3009, "Audi R8 LMS EVO II GT3",        "R8 GT3",          +1.5),
        new(3010, "Ferrari 296 GT3",               "296 GT3",         +0.8),
        new(3011, "McLaren 720S GT3 EVO",          "720S GT3 EVO",    -1.0),
    ];

    // GT4 cars — offset from the Porsche 718 Cayman GT4 MR baseline
    private static readonly List<CarDef> Gt4Cars =
    [
        new(4001, "Mercedes-AMG GT4",                    "AMG GT4",      -0.8),
        new(4002, "BMW M4 G82 GT4 Evo",                  "M4 GT4 Evo",   -0.3),
        new(4003, "Porsche 718 Cayman GT4 Clubsport MR", "718 GT4 MR",    0.0),
        new(4004, "Ford Mustang GT4",                    "Mustang GT4",  +0.5),
        new(4005, "McLaren 570S GT4",                    "570S GT4",     +0.8),
        new(4006, "Aston Martin Vantage GT4",            "Vantage GT4",  +0.3),
    ];

    // IMSA GTP cars — absolute offsets from the GT3 week baseline (negative = faster)
    private static readonly List<CarDef> GtpCars =
    [
        new(5001, "Ferrari 499P",         "499P",          0.0),
        new(5002, "Cadillac V-Series.R GTP", "Cadillac GTP", +0.5),
        new(5003, "Porsche 963 GTP",      "963 GTP",       -0.3),
        new(5004, "BMW M Hybrid V8",      "M Hybrid V8",   +0.3),
        new(5005, "Acura ARX-06 GTP",     "ARX-06 GTP",    -0.2),
        new(5006, "Dallara P217",         "P217",          +0.8),
    ];

    // Sports Car LMP3
    private static readonly List<CarDef> LigierCar =
    [
        new(6001, "Ligier JS P320", "JS P320", 0.0),
    ];

    // Porsche Cup — single car
    private static readonly CarDef PorscheCupCar =
        new(7001, "Porsche 911 Cup (992.2)", "911 Cup", 0.0);

    // ── 2026 S2 week start dates (Wednesday) ─────────────────────────────────
    // Season 2 runs approximately March 11 – June 10, 2026
    private static DateOnly W(int week) =>
        new DateOnly(2026, 3, 11).AddDays((week - 1) * 7);

    // ── Series 1: GT3 Challenge Fixed by Fanatec ─────────────────────────────
    public static readonly SeriesDef Gt3Fixed = new(
        SeriesId: 9001,
        SeasonId: 90001,
        Name:     "GT3 Challenge Fixed by Fanatec",
        Year:     2026,
        Quarter:  2,
        Cars:     Gt3Cars,
        Weeks:
        [
            new(1,  8001, "St. Petersburg Grand Prix",             "Grand Prix",           W(1),   72.5, 2.0),
            new(2,  8002, "Sebring International Raceway",         "International",        W(2),  122.0, 2.5),
            new(3,  8003, "Summit Point Raceway",                  "Summit Point Raceway", W(3),   69.0, 2.0),
            new(4,  8004, "Circuit de Spa-Francorchamps",          "Grand Prix Pits",      W(4),  138.5, 2.5),
            new(5,  8005, "Autodromo Internazionale del Mugello",  "Grand Prix",           W(5),  108.0, 2.0),
            new(6,  8006, "Fuji International Speedway",           "Grand Prix",           W(6),  101.0, 2.0),
            new(7,  8007, "Brands Hatch Circuit",                  "Grand Prix",           W(7),   83.5, 2.0),
            new(8,  8008, "Adelaide Street Circuit",               "",                     W(8),   79.0, 2.0),
            new(9,  8009, "Suzuka International Racing Course",    "Grand Prix",           W(9),  121.0, 2.5),
            new(10, 8010, "Circuit de Nevers Magny-Cours",         "Grand Prix",           W(10),  99.5, 2.0),
            new(11, 8011, "Thruxton Circuit",                      "",                     W(11),  68.0, 2.0),
        ]);

    // ── Series 2: GT Sprint Series by Simucube ───────────────────────────────
    public static readonly SeriesDef GtSprint = new(
        SeriesId: 9002,
        SeasonId: 90002,
        Name:     "GT Sprint Series by Simucube",
        Year:     2026,
        Quarter:  2,
        Cars:     Gt3Cars,
        Weeks:
        [
            new(1,  8001, "St. Petersburg Grand Prix",             "Grand Prix",           W(1),   72.0, 2.0),
            new(2,  8002, "Sebring International Raceway",         "International",        W(2),  121.0, 2.5),
            new(3,  8003, "Summit Point Raceway",                  "Summit Point Raceway", W(3),   69.5, 2.0),
            new(4,  8004, "Circuit de Spa-Francorchamps",          "Grand Prix Pits",      W(4),  137.0, 2.5),
            new(5,  8005, "Autodromo Internazionale del Mugello",  "Grand Prix",           W(5),  107.0, 2.0),
            new(6,  8006, "Fuji International Speedway",           "Grand Prix",           W(6),  100.0, 2.0),
            new(7,  8007, "Brands Hatch Circuit",                  "Grand Prix",           W(7),   83.0, 2.0),
            new(8,  8008, "Adelaide Street Circuit",               "",                     W(8),   78.5, 2.0),
            new(9,  8009, "Suzuka International Racing Course",    "Grand Prix",           W(9),  120.0, 2.5),
            new(10, 8010, "Circuit de Nevers Magny-Cours",         "Grand Prix",           W(10),  98.0, 2.0),
            new(11, 8011, "Thruxton Circuit",                      "",                     W(11),  67.5, 2.0),
        ]);

    // ── Series 3: GT4 Falken Tyre Challenge ──────────────────────────────────
    public static readonly SeriesDef Gt4 = new(
        SeriesId: 9003,
        SeasonId: 90003,
        Name:     "GT4 Falken Tyre Challenge",
        Year:     2026,
        Quarter:  2,
        Cars:     Gt4Cars,
        Weeks:
        [
            new(1,  8008, "Adelaide Street Circuit",               "",                     W(1),   86.0,  2.0),
            new(2,  8002, "Sebring International Raceway",         "International",        W(2),  138.0,  3.0),
            new(3,  8021, "Sonoma Raceway",                        "Sportscar Alt",        W(3),  101.5,  2.0),
            new(4,  8022, "Donington Park Racing Circuit",         "National",             W(4),   71.0,  1.5),
            new(5,  8023, "Barber Motorsports Park",               "",                     W(5),   91.0,  2.0),
            new(6,  8024, "Nurburgring Combined",                  "Gesamtstrecke24h",     W(6),  545.0, 15.0),
            new(7,  8025, "Motorsport Arena Oschersleben",         "Grand Prix",           W(7),   93.0,  2.0),
            new(8,  8017, "WeatherTech Raceway at Laguna Seca",    "Full Course",          W(8),   91.0,  2.0),
            new(9,  8026, "Circuit of the Americas",               "Grand Prix",           W(9),  141.0,  3.0),
            new(10, 8027, "Snetterton Circuit",                    "300",                  W(10), 124.0,  2.5),
            new(11, 8028, "Fuji International Speedway",           "No Chicane",           W(11), 106.0,  2.0),
        ]);

    // ── Series 4: IMSA iRacing Series ────────────────────────────────────────
    // BaseLapSeconds = GT3-class pace. FasterClassOffset = additional seconds
    // subtracted for GTP cars (negative because GTP is faster).
    public static readonly SeriesDef Imsa = new(
        SeriesId:       9004,
        SeasonId:       90004,
        Name:           "IMSA iRacing Series",
        Year:           2026,
        Quarter:        2,
        Cars:           Gt3Cars,
        FasterClassCars: GtpCars,
        Weeks:
        [
            new(1,  8012, "Silverstone Circuit",                        "Grand Prix",           W(1),  121.0, 2.5, -13.0),
            new(2,  8002, "Sebring International Raceway",              "International",        W(2),  123.0, 2.5, -14.0),
            new(3,  8013, "Hockenheimring Baden-Württemberg",           "Grand Prix",           W(3),   99.0, 2.0, -11.0),
            new(4,  8014, "Algarve International Circuit",              "Grand Prix",           W(4),  103.0, 2.0, -10.0),
            new(5,  8015, "Long Beach Street Circuit",                  "",                     W(5),   80.5, 2.0,  -6.0),
            new(6,  8016, "Autodromo Internazionale Enzo e Dino Ferrari","Grand Prix",          W(6),  103.0, 2.0, -12.0),
            new(7,  8017, "WeatherTech Raceway at Laguna Seca",         "Full Course",          W(7),   82.0, 2.0,  -8.0),
            new(8,  8004, "Circuit de Spa-Francorchamps",               "Grand Prix Pits",      W(8),  137.0, 2.5, -15.0),
            new(9,  8018, "Road Atlanta",                               "Full Course",          W(9),  136.0, 2.5, -14.0),
            new(10, 8019, "Nurburgring Combined",                       "Gesamtstrecke Long",   W(10), 495.0, 20.0, -50.0),
            new(11, 8020, "Detroit Grand Prix at Belle Isle",           "Belle Isle",           W(11),  89.0, 2.0,  -9.0),
        ]);

    // ── Series 5: Sports Car Challenge by Falken Tyre ────────────────────────
    // BaseLapSeconds = GT4-class pace. FasterClassOffset = Ligier JS P320 advantage.
    public static readonly SeriesDef SportsCar = new(
        SeriesId:       9005,
        SeasonId:       90005,
        Name:           "Sports Car Challenge by Falken Tyre",
        Year:           2026,
        Quarter:        2,
        Cars:           Gt4Cars,
        FasterClassCars: LigierCar,
        Weeks:
        [
            new(1,  8008, "Adelaide Street Circuit",               "",                     W(1),   86.5,  2.0,  -6.0),
            new(2,  8002, "Sebring International Raceway",         "International",        W(2),  132.0,  3.0, -12.0),
            new(3,  8021, "Sonoma Raceway",                        "Sportscar Alt",        W(3),  101.0,  2.0,  -7.5),
            new(4,  8022, "Donington Park Racing Circuit",         "National",             W(4),   70.5,  1.5,  -5.5),
            new(5,  8023, "Barber Motorsports Park",               "",                     W(5),   91.0,  2.0,  -5.0),
            new(6,  8024, "Nurburgring Combined",                  "Gesamtstrecke24h",     W(6),  545.0, 15.0, -75.0),
            new(7,  8025, "Motorsport Arena Oschersleben",         "Grand Prix",           W(7),   92.5,  2.0,  -5.0),
            new(8,  8017, "WeatherTech Raceway at Laguna Seca",    "Full Course",          W(8),   91.0,  2.0,  -4.5),
            new(9,  8026, "Circuit of the Americas",               "Grand Prix",           W(9),  141.0,  3.0, -10.0),
            new(10, 8027, "Snetterton Circuit",                    "300",                  W(10), 119.0,  2.5,  -9.0),
            new(11, 8028, "Fuji International Speedway",           "No Chicane",           W(11), 106.0,  2.0,  -7.0),
        ]);

    // ── Series 6: iRacing Porsche Cup by CONSPIT ─────────────────────────────
    public static readonly SeriesDef PorscheCup = new(
        SeriesId: 9006,
        SeasonId: 90006,
        Name:     "iRacing Porsche Cup by CONSPIT",
        Year:     2026,
        Quarter:  2,
        Cars:     [PorscheCupCar],
        Weeks:
        [
            new(1,  8002, "Sebring International Raceway",                        "International",  W(1),  122.5, 1.5),
            new(2,  8016, "Autodromo Internazionale Enzo e Dino Ferrari",         "Grand Prix",     W(2),  100.0, 1.5),
            new(3,  8009, "Suzuka International Racing Course",                   "Grand Prix",     W(3),  122.0, 1.5),
            new(4,  8029, "Autodromo Hermanos Rodriguez",                         "Grand Prix",     W(4),  101.0, 1.5),
            new(5,  8015, "Long Beach Street Circuit",                            "",               W(5),   83.0, 1.5),
            new(6,  8030, "Red Bull Ring",                                        "Grand Prix",     W(6),   78.0, 1.0),
            new(7,  8024, "Nurburgring Combined",                                 "Gesamtstrecke24h", W(7), 500.0, 10.0),
            new(8,  8022, "Donington Park Racing Circuit",                        "National",       W(8),   68.0, 1.0),
            new(9,  8004, "Circuit de Spa-Francorchamps",                         "Grand Prix Pits",W(9),  141.0, 2.0),
            new(10, 8031, "Circuit Zandvoort",                                    "Grand Prix",     W(10),  98.0, 1.5),
            new(11, 8008, "Adelaide Street Circuit",                              "",               W(11),  81.0, 1.5),
        ]);

    // ── Series 7: iRacing Porsche Cup Fixed by CONSPIT ───────────────────────
    public static readonly SeriesDef PorscheCupFixed = new(
        SeriesId: 9007,
        SeasonId: 90007,
        Name:     "iRacing Porsche Cup Fixed by CONSPIT",
        Year:     2026,
        Quarter:  2,
        Cars:     [PorscheCupCar with { CarId = 7002, Abbr = "911 Cup (Fixed)" }],
        Weeks:
        [
            new(1,  8002, "Sebring International Raceway",                        "International",  W(1),  123.0, 1.5),
            new(2,  8016, "Autodromo Internazionale Enzo e Dino Ferrari",         "Grand Prix",     W(2),  100.5, 1.5),
            new(3,  8009, "Suzuka International Racing Course",                   "Grand Prix",     W(3),  122.5, 1.5),
            new(4,  8029, "Autodromo Hermanos Rodriguez",                         "Grand Prix",     W(4),  101.5, 1.5),
            new(5,  8015, "Long Beach Street Circuit",                            "",               W(5),   83.5, 1.5),
            new(6,  8030, "Red Bull Ring",                                        "Grand Prix",     W(6),   78.5, 1.0),
            new(7,  8024, "Nurburgring Combined",                                 "Gesamtstrecke24h", W(7), 502.0, 10.0),
            new(8,  8022, "Donington Park Racing Circuit",                        "National",       W(8),   68.5, 1.0),
            new(9,  8004, "Circuit de Spa-Francorchamps",                         "Grand Prix Pits",W(9),  141.5, 2.0),
            new(10, 8031, "Circuit Zandvoort",                                    "Grand Prix",     W(10),  98.5, 1.5),
            new(11, 8008, "Adelaide Street Circuit",                              "",               W(11),  81.5, 1.5),
        ]);

    public static readonly IReadOnlyList<SeriesDef> AllSeries =
    [
        Gt3Fixed, GtSprint, Gt4, Imsa, SportsCar, PorscheCup, PorscheCupFixed,
    ];
}
