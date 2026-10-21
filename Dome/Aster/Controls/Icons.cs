namespace Aster.Controls;

/// <summary>24x24 SVG path 数据集合，对应设计稿中的线性图标。</summary>
public static class Icons
{
    private static string Circle(double cx, double cy, double r)
        => $"M{cx} {cy}m-{r} 0a{r} {r} 0 1 0 {r * 2} 0a{r} {r} 0 1 0 -{r * 2} 0";

    public static readonly string User = Circle(12, 8, 3.6) + "M4.5 20a7.5 7.5 0 0 1 15 0";
    public static readonly string Users = Circle(9, 8, 3.4) +
        "M2.5 20a6.5 6.5 0 0 1 13 0M17 5.2a3.2 3.2 0 0 1 0 5.6M18.5 14.4a5.6 5.6 0 0 1 3 5.1";
    public static readonly string UserPlus = Circle(9, 8, 3.4) +
        "M2.5 20a6.5 6.5 0 0 1 13 0M18 8v6M15 11h6";

    public static readonly string Doc =
        "M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8zM14 3v5h5";
    public static readonly string DocText =
        "M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8zM14 3v5h5M9 13h6M9 17h4";
    public static readonly string Folder =
        "M3 7.5A2 2 0 0 1 5 5.5h4l2 2.5h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z";
    public static readonly string Trash =
        "M4 7h16M9 7V5.2A1.2 1.2 0 0 1 10.2 4h3.6A1.2 1.2 0 0 1 15 5.2V7M6.5 7l.8 12.2A1.8 1.8 0 0 0 9.1 21h5.8a1.8 1.8 0 0 0 1.8-1.8L17.5 7M10 11v6M14 11v6";
    public static readonly string Edit =
        "M12 20h9M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z";
    public static readonly string Star =
        "m12 3.6 2.7 5.5 6 .9-4.3 4.2 1 6-5.4-2.9-5.4 2.9 1-6L3.3 10l6-.9z";
    public static readonly string Clock = Circle(12, 12, 9) + "M12 7v5l3.4 2";

    public static readonly string Music =
        "M9 18V6l12-2v12" + Circle(6, 18, 3) + Circle(18, 16, 3);
    public static readonly string Heart =
        "M12 20s-7-4.4-7-9.2A4 4 0 0 1 12 8.4 4 4 0 0 1 19 10.8C19 15.6 12 20 12 20z";
    public static readonly string Hdd =
        "M4 4h16a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM4 13h16a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-5a1 1 0 0 1 1-1zM7 7.5h.01M7 16.5h.01";
    public static readonly string List =
        "M8.5 6h12M8.5 12h12M8.5 18h12M4 6h.01M4 12h.01M4 18h.01";
    public static readonly string Grid =
        "M4.8 4h5.4a1 1 0 0 1 1 1v5.4a1 1 0 0 1-1 1H4.8a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM13.8 4h5.4a1 1 0 0 1 1 1v5.4a1 1 0 0 1-1 1h-5.4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM4.8 13h5.4a1 1 0 0 1 1 1v5.4a1 1 0 0 1-1 1H4.8a1 1 0 0 1-1-1V14a1 1 0 0 1 1-1zM13.8 13h5.4a1 1 0 0 1 1 1v5.4a1 1 0 0 1-1 1h-5.4a1 1 0 0 1-1-1V14a1 1 0 0 1 1-1z";
    public static readonly string Bars = "M5 12.5v8M12 7.5v13M19 14.5v6";
    public static readonly string Cart =
        "M3 5h2l2.4 10.5h10.2L20 8H6" + Circle(9, 19, 1.5) + Circle(17, 19, 1.5);
    public static readonly string Trend = "M3 17 9 11l4 4 8-8M17 7h4v4";
    public static readonly string Download = "M12 3v12M7 10l5 5 5-5M4 19h16";
    public static readonly string Plus = "M12 5v14M5 12h14";
    public static readonly string Minus = "M4 12h16";
    public static readonly string Square = "M6 5h12a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z";
    public static readonly string Close = "M6 6l12 12M18 6L6 18";
    public static readonly string Check = "M4.5 12.5 9.5 17.5 19.5 6.5";
    public static readonly string ChevronDown = "m6 9 6 6 6-6";
    public static readonly string ChevronRight = "m9 6 6 6-6 6";
    public static readonly string Search = Circle(11, 11, 7) + "M20 20l-3.6-3.6";
    public static readonly string Lock =
        "M4 10.5h16a1 1 0 0 1 1 1V20a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-8.5a1 1 0 0 1 1-1zM8 10.5V7.5a4 4 0 0 1 8 0v3";
    public static readonly string Mail =
        "M3 5h18a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1zM2.2 6.2 12 13.2l9.8-7";
    public static readonly string Eye =
        "M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z" + Circle(12, 12, 2.8);
    public static readonly string EyeOff =
        "M3 3l18 18M10.6 5.2A9.6 9.6 0 0 1 12 5c6.5 0 10 7 10 7a17 17 0 0 1-3.3 4M6.3 7.3A16.6 16.6 0 0 0 2 12s3.5 7 10 7a9.9 9.9 0 0 0 4.4-1";

    public static readonly string Play = "M8 5.2v13.6L19.5 12z";
    public static readonly string Pause = "M7.5 5h3.4v14H7.5zM13.6 5H17v14h-3.4z";
    public static readonly string Prev = "M17 5.2v13.6L7.3 12zM4.6 5.2h1.9v13.6H4.6z";
    public static readonly string Next = "M7 5.2v13.6L16.7 12zM17.5 5.2h1.9v13.6h-1.9z";
    public static readonly string Shuffle =
        "M16 3h5v5M4 20 21 3M21 16v5h-5M15 15l6 6M4 4l5 5";
    public static readonly string Repeat =
        "M17 2.5l3.5 3.5L17 9.5M3.5 11V9.5a3.5 3.5 0 0 1 3.5-3.5h13M7 21.5 3.5 18 7 14.5M20.5 13v1.5a3.5 3.5 0 0 1-3.5 3.5H4";
    public static readonly string Volume =
        "M11 5 6.5 9H3v6h3.5L11 19zM15.5 9a4 4 0 0 1 0 6M18.5 6.5a8 8 0 0 1 0 11";
    public static readonly string More =
        Circle(5, 12, 1.5) + Circle(12, 12, 1.5) + Circle(19, 12, 1.5);

    public static readonly string Bell =
        "M18 8.5a6 6 0 1 0-12 0c0 6-2.5 7.5-2.5 7.5h17S18 14.5 18 8.5zM13.7 20a2 2 0 0 1-3.4 0";
    public static readonly string Database =
        "M12 3c4.4 0 8 1.3 8 3s-3.6 3-8 3-8-1.3-8-3 3.6-3 8-3zM4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3";
    public static readonly string Globe = Circle(12, 12, 9) + "M3 12h18M12 3a15 15 0 0 1 0 18 15 15 0 0 1 0-18z";
    public static readonly string Book =
        "M4 5.5A2.5 2.5 0 0 1 6.5 3H20v15H6.5A2.5 2.5 0 0 0 4 20.5zM4 20.5A2.5 2.5 0 0 1 6.5 18H20v3H6.5";
    public static readonly string Message =
        "M21 11.5a8.4 8.4 0 0 1-9 8.4 9 9 0 0 1-3.3-.7L3 21l1.9-5.1A8.4 8.4 0 0 1 12 3.1a8.4 8.4 0 0 1 9 8.4z";
    public static readonly string Info = Circle(12, 12, 9) + "M12 11v5" + Circle(12, 8, 1);
    public static readonly string Refresh = "M3 12a9 9 0 1 0 3-6.7M3 4v5h5";
    public static readonly string Lightning = "M13 2 4 14h7l-1 8 9-12h-7z";
    public static readonly string PlayCircle = Circle(12, 12, 9) + "M12 8v8M8 12h8";
    public static readonly string Filter = "M4 6h16M7 12h10M10 18h4";
    public static readonly string Sliders =
        "M4 7h3.5M11.5 7H20M4 12h7M15 12h5M4 17h4M12 17h8" +
        Circle(9.5, 7, 2) + Circle(13, 12, 2) + Circle(10, 17, 2);
}
