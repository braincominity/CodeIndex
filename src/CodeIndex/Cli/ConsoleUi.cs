namespace CodeIndex.Cli;

/// <summary>
/// Console UI helpers: spinner, progress bar, banner, and easter egg messages.
/// コンソールUIヘルパー: スピナー、プログレスバー、バナー、イースターエッグメッセージ。
/// </summary>
public static class ConsoleUi
{
    // --- Spinner / スピナー ---

    /// <summary>
    /// Start spinner on a background thread, returns CancellationTokenSource to stop it.
    /// バックグラウンドスレッドでスピナーを開始。停止用のCancellationTokenSourceを返す。
    /// </summary>
    public static CancellationTokenSource? StartSpinner(string message, string[] frames)
    {
        // Braille frames are single-char; themed frames are longer strings containing the display text
        // ブレイルフレームは1文字、テーマフレームは表示テキストを含む長い文字列
        bool isThemed = frames.Length > 0 && frames[0].Length > 2;

        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(message);
            return null;
        }

        var cts = new CancellationTokenSource();
        var ct = cts.Token;
        Task.Run(() =>
        {
            int i = 0;
            while (!ct.IsCancellationRequested)
            {
                var frame = frames[i % frames.Length];
                var line = isThemed ? $"\r{frame}" : $"\r{frame} {message}";
                Console.Write(line);
                Console.Out.Flush();
                i++;
                try { Task.Delay(100, ct).Wait(ct); } catch (OperationCanceledException) { break; }
            }
        }, ct);
        return cts;
    }

    /// <summary>
    /// Stop spinner and clear the line.
    /// スピナーを停止して行をクリア。
    /// </summary>
    public static void StopSpinner(CancellationTokenSource? cts)
    {
        if (cts == null) return;
        cts.Cancel();
        // Small delay to let the spinner task exit / スピナータスク終了のための短い待機
        Thread.Sleep(20);
        if (!Console.IsOutputRedirected)
        {
            Console.Write($"\r{new string(' ', GetWindowWidth() - 1)}\r");
            Console.Out.Flush();
        }
        cts.Dispose();
    }

    /// <summary>
    /// Get spinner frames based on easter egg flag.
    /// イースターエッグフラグに基づくスピナーフレームを取得。
    /// </summary>
    public static string[] GetSpinnerFrames(string? easterEgg) => easterEgg switch
    {
        "--sushi" =>
        [
            "\U0001f363 Slicing       ", "\U0001f363 Slicing.      ", "\U0001f363 Slicing..     ", "\U0001f363 Slicing...    ",
            "\U0001f363 Shaping       ", "\U0001f363 Shaping.      ", "\U0001f363 Shaping..     ", "\U0001f363 Shaping...    ",
            "\U0001f363 Pressing      ", "\U0001f363 Pressing.     ", "\U0001f363 Pressing..    ", "\U0001f363 Pressing...   ",
            "\U0001f363 Itadakimasu!  ",
        ],
        "--coffee" =>
        [
            "\u2615 Grinding      ", "\u2615 Grinding.     ", "\u2615 Grinding..    ", "\u2615 Grinding...   ",
            "\u2615 Heating       ", "\u2615 Heating.      ", "\u2615 Heating..     ", "\u2615 Heating...    ",
            "\u2615 Brewing       ", "\u2615 Brewing.      ", "\u2615 Brewing..     ", "\u2615 Brewing...    ",
        ],
        "--ramen" =>
        [
            "\U0001f35c Boiling       ", "\U0001f35c Boiling.      ", "\U0001f35c Boiling..     ", "\U0001f35c Boiling...    ",
            "\U0001f35c Steaming      ", "\U0001f35c Steaming.     ", "\U0001f35c Steaming..    ", "\U0001f35c Steaming...   ",
            "\U0001f35c Slurping      ", "\U0001f35c Slurping.     ", "\U0001f35c Slurping..    ", "\U0001f35c Slurping...   ",
            "\U0001f35c Itadakimasu!  ",
        ],
        "--wine" =>
        [
            "\U0001f377 Crushing      ", "\U0001f377 Crushing.     ", "\U0001f377 Crushing..    ", "\U0001f377 Crushing...   ",
            "\U0001f377 Aging         ", "\U0001f377 Aging.        ", "\U0001f377 Aging..       ", "\U0001f377 Aging...      ",
            "\U0001f377 Pouring       ", "\U0001f377 Pouring.      ", "\U0001f377 Pouring..     ", "\U0001f377 Pouring...    ",
            "\U0001f377 Sant\u00e9!        ",
        ],
        "--beer" =>
        [
            "\U0001f37a Tapping       ", "\U0001f37a Tapping.      ", "\U0001f37a Tapping..     ", "\U0001f37a Tapping...    ",
            "\U0001f37a Pouring       ", "\U0001f37a Pouring.      ", "\U0001f37a Pouring..     ", "\U0001f37a Pouring...    ",
            "\U0001f37a Foaming       ", "\U0001f37a Foaming.      ", "\U0001f37a Foaming..     ", "\U0001f37a Foaming...    ",
            "\U0001f37a Cheers!       ",
        ],
        "--matcha" =>
        [
            "\U0001f375 Sifting       ", "\U0001f375 Sifting.      ", "\U0001f375 Sifting..     ", "\U0001f375 Sifting...    ",
            "\U0001f375 Pouring       ", "\U0001f375 Pouring.      ", "\U0001f375 Pouring..     ", "\U0001f375 Pouring...    ",
            "\U0001f375 Whisking      ", "\U0001f375 Whisking.     ", "\U0001f375 Whisking..    ", "\U0001f375 Whisking...   ",
            "\U0001f375 Douzo!        ",
        ],
        "--whisky" =>
        [
            "\U0001f943 Mashing       ", "\U0001f943 Mashing.      ", "\U0001f943 Mashing..     ", "\U0001f943 Mashing...    ",
            "\U0001f943 Distilling    ", "\U0001f943 Distilling.   ", "\U0001f943 Distilling..  ", "\U0001f943 Distilling... ",
            "\U0001f943 Aging         ", "\U0001f943 Aging.        ", "\U0001f943 Aging..       ", "\U0001f943 Aging...      ",
            "\U0001f943 Slainte!      ",
        ],
        // Default: Braille spinner / デフォルト: ブレイルスピナー
        _ => ["\u2807", "\u280b", "\u280f", "\u2838", "\u280f", "\u2839"],
    };

    // --- Progress bar / プログレスバー ---

    // Active spinner frames for progress bar (themed or default braille)
    // プログレスバー用アクティブスピナーフレーム（テーマ付きまたはデフォルトブレイル）
    private static string[] _progressSpinnerFrames = ["\u2807", "\u280b", "\u280f", "\u2838", "\u280f", "\u2839"];
    // Track last progress line length for clearing / クリア用に最後のプログレス行の長さを記録
    private static int _lastProgressLineLength;

    /// <summary>
    /// Set progress bar spinner theme (reuses GetSpinnerFrames).
    /// プログレスバーのスピナーテーマを設定（GetSpinnerFramesを再利用）。
    /// </summary>
    public static void SetProgressTheme(string? easterEgg)
    {
        _progressSpinnerFrames = GetSpinnerFrames(easterEgg);
    }

    /// <summary>
    /// Print inline progress bar with spinner.
    /// スピナー付きインライン進捗バーを表示。
    /// </summary>
    public static void PrintProgress(int current, int total)
    {
        // Update every 50 files or at completion / 50ファイルごと、または完了時に更新
        if (current % 50 != 0 && current != total)
            return;

        const int barWidth = 32;
        var pct = (double)current / total;
        int filled = (int)Math.Round(pct * barWidth);
        if (filled > barWidth) filled = barWidth;

        // Show spinner while in progress, checkmark on completion / 処理中はスピナー、完了時はチェックマーク
        var spinner = current == total ? " " : _progressSpinnerFrames[(current / 50) % _progressSpinnerFrames.Length];
        var bar = new string('\u2588', filled) + new string('\u2591', barWidth - filled);
        var line = $"{spinner} {bar} {pct * 100,5:F1}%  [{current:N0}/{total:N0}]";

        if (!Console.IsOutputRedirected)
        {
            Console.Write($"\r{line}");
            Console.Out.Flush();
            _lastProgressLineLength = line.Length;
            if (current == total)
            {
                Console.WriteLine();
                _lastProgressLineLength = 0;
            }
        }
        else
        {
            // Fallback for redirected output / リダイレクト時はフォールバック
            Console.WriteLine(line.TrimStart());
        }
    }

    /// <summary>
    /// Clear the current progress bar line so other output can be printed cleanly.
    /// 他の出力を正しく表示するために現在のプログレスバー行をクリア。
    /// </summary>
    public static void ClearProgressLine()
    {
        if (!Console.IsOutputRedirected && _lastProgressLineLength > 0)
        {
            Console.Write($"\r{new string(' ', _lastProgressLineLength)}\r");
            Console.Out.Flush();
            _lastProgressLineLength = 0;
        }
    }

    /// <summary>
    /// Print a warning message, clearing the progress bar line first if needed.
    /// 必要に応じてプログレスバー行をクリアしてから警告メッセージを表示。
    /// </summary>
    public static void PrintWarning(string message)
    {
        ClearProgressLine();
        Console.Error.WriteLine($"  [WARN] {message}");
    }

    // --- Banner / バナー ---

    /// <summary>
    /// Print ASCII-art banner.
    /// ASCIIアートバナーを表示。
    /// </summary>
    public static void PrintBanner()
    {
        const string banner = """

             ██████╗ ██████╗ ██████╗ ███████╗██╗███╗   ██╗██████╗ ███████╗██╗  ██╗
            ██╔════╝██╔═══██╗██╔══██╗██╔════╝██║████╗  ██║██╔══██╗██╔════╝╚██╗██╔╝
            ██║     ██║   ██║██║  ██║█████╗  ██║██╔██╗ ██║██║  ██║█████╗   ╚███╔╝
            ██║     ██║   ██║██║  ██║██╔══╝  ██║██║╚██╗██║██║  ██║██╔══╝   ██╔██╗
            ╚██████╗╚██████╔╝██████╔╝███████╗██║██║ ╚████║██████╔╝███████╗██╔╝ ██╗
             ╚═════╝ ╚═════╝ ╚═════╝ ╚══════╝╚═╝╚═╝  ╚═══╝╚═════╝ ╚══════╝╚═╝  ╚═╝
            """;
        Console.WriteLine(banner);
    }

    // --- Easter eggs / イースターエッグ ---

    /// <summary>
    /// Print easter egg message (standalone mode).
    /// イースターエッグメッセージを表示（単体実行時）。
    /// </summary>
    public static void PrintEasterEggMessage(string flag)
    {
        var (en, ja) = flag switch
        {
            "--sushi"  => ("\U0001f363 Indexing is like making sushi \u2014 patience yields perfection.",
                           "   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u306f\u5bff\u53f8\u4f5c\u308a\u306e\u3088\u3046\u306b \u2014 \u5fcd\u8010\u304c\u5b8c\u74a7\u3092\u751f\u3080\u3002"),
            "--coffee" => ("\u2615 Leave the indexing to me and go grab a coffee!",
                           "   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u306f\u4efb\u305b\u3066\u3001\u30b3\u30fc\u30d2\u30fc\u3067\u3082\u98f2\u3093\u3067\u304d\u3066\uff01"),
            "--ramen"  => ("\U0001f35c Indexing in progress... perfect time for a bowl of ramen!",
                           "   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u4e2d\u2026\u30e9\u30fc\u30e1\u30f3\u4e00\u676f\u3044\u304b\u304c\uff1f"),
            "--wine"   => ("\U0001f377 Crushing... Aging... Pouring... Sant\u00e9!",
                           "   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u306f\u30ef\u30a4\u30f3\u306e\u3088\u3046\u306b\u2014\u719f\u6210\u3092\u5f85\u3064\u4fa1\u5024\u304c\u3042\u308b\u3002"),
            "--beer"   => ("\U0001f37a Tapping... Pouring... Foaming... Cheers!",
                           "   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u5b8c\u4e86\u307e\u3067\u3001\u4e7e\u676f\uff01"),
            "--matcha" => ("\U0001f375 Sifting... Pouring... Whisking... \u3069\u3046\u305e\uff01",
                           "   \u4e00\u670d\u306e\u62b9\u8336\u3067\u3082\u3044\u304b\u304c\u3067\u3059\u304b\uff1f"),
            "--whisky" => ("\U0001f943 Mashing... Distilling... Aging... Slainte!",
                           "   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u306f\u30a6\u30a4\u30b9\u30ad\u30fc\u306e\u3088\u3046\u306b\u2014\u719f\u6210\u304c\u5927\u4e8b\u3002"),
            _ => ("", ""),
        };
        Console.WriteLine(en);
        Console.WriteLine(ja);
    }

    // --- Version loading / バージョン読み込み ---

    /// <summary>
    /// Load version from version.json.
    /// version.jsonからバージョンを読み込み。
    /// </summary>
    public static string LoadVersion()
    {
        var exeDir = AppContext.BaseDirectory;
        var path = Path.Combine(exeDir, "version.json");
        if (!File.Exists(path))
        {
            // Fallback: look relative to current directory / カレントディレクトリからの相対パスでフォールバック
            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.json");
        }
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("version", out var ver))
                return ver.GetString() ?? "0.0.0";
        }
        return "0.0.0";
    }

    // --- Usage / 使い方 ---

    /// <summary>
    /// Print usage information.
    /// 使い方を表示する。
    /// </summary>
    public static void PrintUsage(bool showBanner = true)
    {
        if (showBanner)
        {
            PrintBanner();
        }

        Console.WriteLine("Usage:");
        Console.WriteLine("  cdidx <projectPath>");
        Console.WriteLine("  cdidx index <projectPath> [--db <path>] [--rebuild] [--verbose] [--json]");
        Console.WriteLine("  cdidx index <projectPath> --commits <id> [id ...] [--db <path>] [--verbose] [--json]");
        Console.WriteLine("  cdidx index <projectPath> --files <path> [path ...] [--db <path>] [--verbose] [--json]");
        Console.WriteLine("  cdidx search <query> [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--fts]");
        Console.WriteLine("  cdidx symbols [query] [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--kind <kind>]");
        Console.WriteLine("  cdidx files [query] [--db <path>] [--json] [--limit <n>] [--lang <lang>]");
        Console.WriteLine("  cdidx status [--db <path>] [--json]");
        Console.WriteLine("  cdidx mcp [--db <path>]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  index <projectPath>        Build or update the index for a project");
        Console.WriteLine("  search <query>             Full-text search across indexed chunks");
        Console.WriteLine("  symbols [query]            Search symbols (functions, classes, imports)");
        Console.WriteLine("  files [query]              List indexed files");
        Console.WriteLine("  status                     Show database statistics");
        Console.WriteLine("  mcp                        Start MCP server (for AI tools: Claude, Cursor, etc.)");
        Console.WriteLine();
        Console.WriteLine("Index and update options:");
        Console.WriteLine("  --db <path>                Database file path (default for index: <projectPath>/.cdidx/codeindex.db)");
        Console.WriteLine("  --rebuild                  Delete existing DB and rebuild from scratch");
        Console.WriteLine("  --verbose                  Show per-file status ([OK  ]/[SKIP]/[DEL ]/[ERR ])");
        Console.WriteLine("  --json                     Output results as JSON (for AI/machine use)");
        Console.WriteLine("  --commits <id> [id ...]    Update only files changed in the specified git commits");
        Console.WriteLine("  --files <path> [path ...]  Update only the specified files (relative or absolute)");
        Console.WriteLine("  --help, -h                 Show this help message");
        Console.WriteLine("  --version, -V              Show version information");
        Console.WriteLine();
        Console.WriteLine("Update workflows:");
        Console.WriteLine("  Use --commits with a project path to update only files changed by specific commits.");
        Console.WriteLine("  Use --files with a project path to update only specific files.");
        Console.WriteLine();
        Console.WriteLine("Query options:");
        Console.WriteLine("  --db <path>                Database file path (default: .cdidx/codeindex.db in current directory)");
        Console.WriteLine("  --json                     Output as JSON lines (for AI/machine use)");
        Console.WriteLine("  --limit <n>                Max results to return (default: 20)");
        Console.WriteLine("  --lang <lang>              Filter by language");
        Console.WriteLine("  --fts                      Use raw FTS5 query syntax for search");
        Console.WriteLine("  --kind <kind>              Filter symbols by kind (function/class/import)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  cdidx ./myproject                             Index a project");
        Console.WriteLine("  cdidx index ./myproject --commits abc123      Update DB from one commit");
        Console.WriteLine("  cdidx index ./myproject --commits abc123 def456");
        Console.WriteLine("                                              Update DB from multiple commits");
        Console.WriteLine("  cdidx index ./myproject --files src/app.cs    Update specific files");
        Console.WriteLine("  cdidx search \"authenticate\"                    Full-text search");
        Console.WriteLine("  cdidx symbols UserService --kind class         Find class definitions");
        Console.WriteLine("  cdidx files --lang python                      List Python files");
        Console.WriteLine("  cdidx status --json                            DB stats as JSON");
    }

    // --- Helpers / ヘルパー ---

    /// <summary>
    /// Get console window width safely (some environments throw IOException).
    /// コンソール幅を安全に取得する（一部環境ではIOExceptionが発生する）。
    /// </summary>
    private static int GetWindowWidth()
    {
        try
        {
            var w = Console.WindowWidth;
            return w > 0 ? w : 80;
        }
        catch
        {
            return 80;
        }
    }
}
