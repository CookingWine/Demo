// 文件：Icons.cs
// 作用：集中存放全部 24x24 SVG path 字符串，供 Icon 控件通过 {x:Static ctrl:Icons.Xxx} 绑定。
// 使用方：所有视图与窗口的图标都来自这里，替换图标只需改常量而不用改 XAML。
// 约定：坐标系固定 24x24，线宽由 Icon.Thickness 统一控制，因此这里只描述几何、不含颜色与宽度。
// 注意：path 采用相对指令（a/l/h/v）书写，改动坐标时需留意相对基准点，否则整套图标会整体偏移。
namespace Aster.Controls;

/// <summary>24x24 SVG path 数据集合，对应设计稿中的线性图标。</summary>
public static class Icons
{
    // 圆是最高频的图元（头像、搜索、省略号），用函数拼装避免每个图标手写一大串弧线指令。
    // 路径含义：M 移到圆心 -> m 相对移到圆周左端点 -> 两段 180° 圆弧首尾相接拼成整圆。
    // 参数 1 0 分别是 large-arc-flag=1 与 sweep-flag=0（逆时针），两段正好各画半圆。
    // 末尾不写 Z：描边时靠 Icon 的 PenLineCap.Round 让接缝自然闭合，也方便后面继续追加子路径。
    private static string Circle(double cx, double cy, double r)
        => $"M{cx} {cy}m-{r} 0a{r} {r} 0 1 0 {r * 2} 0a{r} {r} 0 1 0 -{r * 2} 0";

    // 账号类：圆头 + 一条肩线。肩线用 a7.5 7.5 圆弧而不是直线，才有设计稿里「半圆托住头像」的感觉。
    public static readonly string User = Circle(12, 8, 3.6) + "M4.5 20a7.5 7.5 0 0 1 15 0";
    public static readonly string Users = Circle(9, 8, 3.4) +
        "M2.5 20a6.5 6.5 0 0 1 13 0M17 5.2a3.2 3.2 0 0 1 0 5.6M18.5 14.4a5.6 5.6 0 0 1 3 5.1";
    public static readonly string UserPlus = Circle(9, 8, 3.4) +
        "M2.5 20a6.5 6.5 0 0 1 13 0M18 8v6M15 11h6";

    // 文档类：外层轮廓用 2 半径圆角矩形（a2 2 0 0 0），右上角通过 M14 3v5h5 表达「折角」，
    // 折角与主体是两个子路径，描边时不会互相闭合，视觉上就是一张折角纸。
    public static readonly string Doc =
        "M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8zM14 3v5h5";
    public static readonly string DocText =
        "M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8zM14 3v5h5M9 13h6M9 17h4";
    public static readonly string Folder =
        "M3 7.5A2 2 0 0 1 5 5.5h4l2 2.5h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z";
    // 垃圾桶：一条桶盖横线 + 提手圆角环 + 桶身收窄的斜边 + 两条内壁竖线，三段子路径共用同一套描边。
    public static readonly string Trash =
        "M4 7h16M9 7V5.2A1.2 1.2 0 0 1 10.2 4h3.6A1.2 1.2 0 0 1 15 5.2V7M6.5 7l.8 12.2A1.8 1.8 0 0 0 9.1 21h5.8a1.8 1.8 0 0 0 1.8-1.8L17.5 7M10 11v6M14 11v6";
    // 编辑：M12 20h9 是底部的书写基线，笔身由两段弧（a2.1 2.1 0 0 1 3 3）构成圆头笔尖。
    public static readonly string Edit =
        "M12 20h9M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z";
    // 星形：用相对坐标 l 依次走 10 个顶点（5 外角 + 5 内角）一笔闭合，z 负责回到起点。
    public static readonly string Star =
        "m12 3.6 2.7 5.5 6 .9-4.3 4.2 1 6-5.4-2.9-5.4 2.9 1-6L3.3 10l6-.9z";
    // 时钟：外圈直接复用 Circle，M12 7v5l3.4 2 是「先向下再向右上」的指针折线。
    public static readonly string Clock = Circle(12, 12, 9) + "M12 7v5l3.4 2";

    // 音乐：两个 Circle 充当符头，M9 18V6l12-2v12 一次画出两条符干与顶部连梁（12-2 让它略向上倾斜）。
    public static readonly string Music =
        "M9 18V6l12-2v12" + Circle(6, 18, 3) + Circle(18, 16, 3);
    // 爱心：两段三次贝塞尔（s 指令复用上一控制点的镜像）拼出左右对称的心形，最后 z 闭合尖角。
    public static readonly string Heart =
        "M12 20s-7-4.4-7-9.2A4 4 0 0 1 12 8.4 4 4 0 0 1 19 10.8C19 15.6 12 20 12 20z";
    // 硬盘：两个圆角矩形叠放；末尾 M7 7.5h.01 是长度仅 0.01 的线段，
    // 配合 Icon 的 Round 线帽渲染成一个圆点，用极小的数据量表达指示灯。
    public static readonly string Hdd =
        "M4 4h16a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM4 13h16a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-5a1 1 0 0 1 1-1zM7 7.5h.01M7 16.5h.01";
    // 列表：三行结构相同，每行先一条长横线，再用 h.01 零长度线段画出行首圆点。
    public static readonly string List =
        "M8.5 6h12M8.5 12h12M8.5 18h12M4 6h.01M4 12h.01M4 18h.01";
    // 宫格：四个 1 半径圆角方块，每段都是「M 起点 h 宽 a 圆角 v 高 a 圆角 ...」的固定写法，只改起点坐标。
    public static readonly string Grid =
        "M4.8 4h5.4a1 1 0 0 1 1 1v5.4a1 1 0 0 1-1 1H4.8a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM13.8 4h5.4a1 1 0 0 1 1 1v5.4a1 1 0 0 1-1 1h-5.4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM4.8 13h5.4a1 1 0 0 1 1 1v5.4a1 1 0 0 1-1 1H4.8a1 1 0 0 1-1-1V14a1 1 0 0 1 1-1zM13.8 13h5.4a1 1 0 0 1 1 1v5.4a1 1 0 0 1-1 1h-5.4a1 1 0 0 1-1-1V14a1 1 0 0 1 1-1z";
    // 柱状：三根不同高度的竖线，没有圆头指令，圆头效果完全来自 PenLineCap.Round。
    public static readonly string Bars = "M5 12.5v8M12 7.5v13M19 14.5v6";
    // 购物车：车体是一条开口折线（M3 5h2l...），两个 Circle 是轮子；
    // 轮半径 1.5 已接近线宽的一半，描边后视觉上接近实心圆。
    public static readonly string Cart =
        "M3 5h2l2.4 10.5h10.2L20 8H6" + Circle(9, 19, 1.5) + Circle(17, 19, 1.5);
    // 趋势：一条上升折线 + 末端「M17 7h4v4」画出的直角箭头，暗示右上方向。
    public static readonly string Trend = "M3 17 9 11l4 4 8-8M17 7h4v4";
    // 货币：Y 形两撇（M7 4.5 12 11l5-6.5）+ 主竖线 + 底部两条等长横线，构成美元符号。
    public static readonly string Currency = "M7 4.5 12 11l5-6.5M12 11v9M7.5 15.2h9M7.5 18.4h9";
    // 下载：向下的箭头（竖直主干 + 两侧翼）再落一条托盘横线。
    public static readonly string Download = "M12 3v12M7 10l5 5 5-5M4 19h16";
    // 加号 / 减号：两个互相垂直的线段，交叉处靠 Round 连接避免出现硬角。
    public static readonly string Plus = "M12 5v14M5 12h14";
    // 上箭头：竖干 + 三角箭头（两段斜线各 6.5 宽），用于「回到顶部」等场景。
    public static readonly string ArrowUp = "M12 19V5M5.5 11.5 12 5l6.5 6.5";
    // 单条横线即可，尺寸由 Icon 的 Width/Height 决定。
    public static readonly string Minus = "M4 12h16";
    // 方框：1 半径圆角的矩形，用于「最大化」按钮。
    public static readonly string Square = "M6 5h12a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z";
    // 关闭：两条对角线，起点分别在左上与右上，构成 ×。
    public static readonly string Close = "M6 6l12 12M18 6L6 18";
    // 对勾：三个点两段折线，拐点在 (9.5,17.5)，比标准 check 更扁平。
    public static readonly string Check = "M4.5 12.5 9.5 17.5 19.5 6.5";
    // 展开/收起箭头：用相对指令 m/l 书写，便于整体平移复用。
    public static readonly string ChevronDown = "m6 9 6 6 6-6";
    // 右侧箭头，用于「进入详情」的指示。
    public static readonly string ChevronRight = "m9 6 6 6-6 6";
    // 搜索：放大镜手柄是一条 45° 斜线（M20 20l-3.6-3.6），与圆环略有重叠以保证视觉连贯。
    public static readonly string Search = Circle(11, 11, 7) + "M20 20l-3.6-3.6";
    // 锁：下方是圆角矩形锁体，上方锁梁用半圆（a4 4 0 0 1 8 0）加两条竖直边接入锁体。
    public static readonly string Lock =
        "M4 10.5h16a1 1 0 0 1 1 1V20a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-8.5a1 1 0 0 1 1-1zM8 10.5V7.5a4 4 0 0 1 8 0v3";
    // 邮件：信封外框 + M2.2 6.2 12 13.2l9.8-7 组成的 V 形封口，两个子路径叠加即为完整的信封。
    public static readonly string Mail =
        "M3 5h18a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1zM2.2 6.2 12 13.2l9.8-7";
    // 眼睛：上下两段对称的 s 曲线夹出橄榄形轮廓，中间再叠一个 Circle 当作瞳孔。
    public static readonly string Eye =
        "M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z" + Circle(12, 12, 2.8);
    // 闭眼：先一条 45° 斜杠表示「划掉」，再用 A 圆弧画出被遮挡后残缺的眼形轮廓（故意不闭合）。
    public static readonly string EyeOff =
        "M3 3l18 18M10.6 5.2A9.6 9.6 0 0 1 12 5c6.5 0 10 7 10 7a17 17 0 0 1-3.3 4M6.3 7.3A16.6 16.6 0 0 0 2 12s3.5 7 10 7a9.9 9.9 0 0 0 4.4-1";

    // 播放：单个闭合三角形，配合 <ctrl:Icon Filled="True"/> 使用；描边模式下会呈现空心三角。
    public static readonly string Play = "M8 5.2v13.6L19.5 12z";
    // 暂停：两个等宽矩形，各自独立的子路径，避免中间被连起来。
    public static readonly string Pause = "M7.5 5h3.4v14H7.5zM13.6 5H17v14h-3.4z";
    // 上一首：三角指向左侧（L 目标在左边）+ 左侧一条竖线当作「到顶」标记。
    public static readonly string Prev = "M17 5.2v13.6L7.3 12zM4.6 5.2h1.9v13.6H4.6z";
    // 下一首：与 Prev 镜像，三角指向右侧。
    public static readonly string Next = "M7 5.2v13.6L16.7 12zM17.5 5.2h1.9v13.6h-1.9z";
    // 随机播放：两条对角交叉线，两端各用 h5/v5 折出箭头，中间一小段斜线补全交叉处。
    public static readonly string Shuffle =
        "M16 3h5v5M4 20 21 3M21 16v5h-5M15 15l6 6M4 4l5 5";
    // 循环：两段「弧 + 圆角箭头」上下对称，形成首尾相接的环路。
    public static readonly string Repeat =
        "M17 2.5l3.5 3.5L17 9.5M3.5 11V9.5a3.5 3.5 0 0 1 3.5-3.5h13M7 21.5 3.5 18 7 14.5M20.5 13v1.5a3.5 3.5 0 0 1-3.5 3.5H4";
    // 音量：喇叭由梯形（M11 5 6.5 9H3v6h3.5L11 19z）加两条逐渐变大的弧组成，弧越多表示音量越大。
    public static readonly string Volume =
        "M11 5 6.5 9H3v6h3.5L11 19zM15.5 9a4 4 0 0 1 0 6M18.5 6.5a8 8 0 0 1 0 11";
    // 更多：三个独立的 Circle，半径 1.5 在 24 网格中视觉上正好是「点」而不是「圈」。
    public static readonly string More =
        Circle(5, 12, 1.5) + Circle(12, 12, 1.5) + Circle(19, 12, 1.5);

    // 铃铛：钟体用 a6 6 弧收出顶部圆肩，两侧下垂到 y=16 后接横线，末尾小弧是铃舌。
    public static readonly string Bell =
        "M18 8.5a6 6 0 1 0-12 0c0 6-2.5 7.5-2.5 7.5h17S18 14.5 18 8.5zM13.7 20a2 2 0 0 1-3.4 0";
    // 数据库：顶部一个压扁的椭圆当作桶口，两条侧边竖线 + 中间两段弧表达桶身与分层。
    public static readonly string Database =
        "M12 3c4.4 0 8 1.3 8 3s-3.6 3-8 3-8-1.3-8-3 3.6-3 8-3zM4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3";
    // 地球：外圈 Circle + 赤道横线；M12 3a15 15 0 0 1 0 18 与对称弧共同近似出一条经线椭圆。
    public static readonly string Globe = Circle(12, 12, 9) + "M3 12h18M12 3a15 15 0 0 1 0 18 15 15 0 0 1 0-18z";
    // 书：左侧书脊用 2.5 半径圆角，右侧竖线是书页边缘，底部一段弧表现翻开的书页厚度。
    public static readonly string Book =
        "M4 5.5A2.5 2.5 0 0 1 6.5 3H20v15H6.5A2.5 2.5 0 0 0 4 20.5zM4 20.5A2.5 2.5 0 0 1 6.5 18H20v3H6.5";
    // 消息气泡：主体是圆角矩形，左下角通过 L3 21l1.9-5.1 拉出尖角，整体一笔闭合。
    public static readonly string Message =
        "M21 11.5a8.4 8.4 0 0 1-9 8.4 9 9 0 0 1-3.3-.7L3 21l1.9-5.1A8.4 8.4 0 0 1 12 3.1a8.4 8.4 0 0 1 9 8.4z";
    // 信息：外圈 Circle + 字母 i 的竖干 + 顶部 Circle(12,8,1) 画出的圆点，比 h.01 更像实心点。
    public static readonly string Info = Circle(12, 12, 9) + "M12 11v5" + Circle(12, 8, 1);
    // 刷新：a9 9 0 1 0 3-6.7 只画一段大圆弧（large-arc=1），末端接 M3 4v5h5 折出箭头。
    public static readonly string Refresh = "M3 12a9 9 0 1 0 3-6.7M3 4v5h5";
    // 闪电：单条闭合多边形，交替使用长短边制造尖锐的折角。
    public static readonly string Lightning = "M13 2 4 14h7l-1 8 9-12h-7z";
    // 注意：这是「圆 + 十字」而非播放三角，实际用作圆形加号按钮。
    public static readonly string PlayCircle = Circle(12, 12, 9) + "M12 8v8M8 12h8";
    // 筛选：三条长度递减的横线，视觉上形成漏斗的横截面。
    public static readonly string Filter = "M4 6h16M7 12h10M10 18h4";
    // 滑杆：三条轨道（横线）+ 三个 Circle 旋钮，旋钮位置故意不在轨道中点，暗示可调节。
    public static readonly string Sliders =
        "M4 7h3.5M11.5 7H20M4 12h7M15 12h5M4 17h4M12 17h8" +
        Circle(9.5, 7, 2) + Circle(13, 12, 2) + Circle(10, 17, 2);
}
