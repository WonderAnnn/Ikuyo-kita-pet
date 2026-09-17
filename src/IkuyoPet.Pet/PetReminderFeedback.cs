using IkuyoPet.Core.Reminders;

namespace IkuyoPet.Pet;

/// <summary>
/// Original completion feedback written for the bright, friendly and energetic
/// character direction of 喜多郁代. The pools stay separate so water and
/// activity reminders can never show one another's completion copy.
/// </summary>
public static class PetReminderFeedback
{
    public static IReadOnlyList<string> WaterCompletionMessages { get; } =
        Array.AsReadOnly(
        [
            "喝到水啦？补满能量，今天也要闪闪发光哦！(๑•̀ㅂ•́)و✧",
            "水分补给完成！走，我们把接下来的小目标也拿下～(｡•̀ᴗ-)✧",
            "咕咚咕咚，状态回来了！你做得超棒的呀～(≧▽≦)",
            "喝水打卡成功！保持这个节奏，节奏感满分～( •̀ ω •́ )✧",
            "水杯空一点，精神亮一点！下一步也一起加油吧(๑˃̵ᴗ˂̵)و",
            "及时补水完成！今天的你也很可靠呢～(｡･ω･｡)ﾉ♡",
            "好耶，喝水完成！把清爽带回工作里吧(☆▽☆)",
            "水分到位，灵感也要跟上！慢慢来，我们一起走～(ง •̀_•́)ง",
            "这口水喝得刚刚好，继续保持漂亮状态呀！(✧∀✧)",
            "补水任务完成！给认真坚持的你一个大大的赞～(๑´ڡ๑)",
            "喝完水就更有元气啦，今天一定能顺利推进的！( •̀ᄇ• ́)ﻭ✧",
            "水水补充完毕！别忘了偶尔抬头看看远方哦～(｡•ㅅ•｡)",
            "做到啦！身体收到补给，接下来也轻快一点吧(ﾉ◕ヮ◕)ﾉ*:･ﾟ✧",
            "好棒的补水节奏！像排练一样，一点点就很稳～(๑•̀ᗝ•́)૭",
            "咕咚一下，疲惫退场！你的状态正在重新闪亮(≧ω≦)/",
            "喝水完成，清爽上线！接下来也拜托你啦～(人 •͈ᴗ•͈)",
            "这一杯是给自己的小小加油，完成得漂亮！(๑˘︶˘๑)",
            "补水成功！把好状态带着走，今天也会很顺的～(ﾉ´ヮ`)ﾉ*:･ﾟ✧",
            "水分已经补上啦，记得继续照顾自己哦！(｡•̀ᴗ-)و ̑̑✧",
            "太好了，喝水动作完成！和我一起保持元气吧～(≧◡≦)"
        ]);

    public static IReadOnlyList<string> ActivityCompletionMessages { get; } =
        Array.AsReadOnly(
        [
            "休息完成！肩膀放松过后，回来会更有劲哦～(๑•̀ㅂ•́)و✧",
            "动一动、喘口气，状态重新上线啦！(｡•̀ᴗ-)✧",
            "很棒！你把自己照顾好啦，接下来继续轻快前进吧～(≧▽≦)",
            "休息打卡成功！像排练间隙一样，充好电再出发( •̀ ω •́ )✧",
            "手腕和眼睛都放松到了吗？完成得很漂亮呀(๑˃̵ᴗ˂̵)و",
            "活动完成！身体醒过来，灵感也跟着来啦～(｡･ω･｡)ﾉ♡",
            "好耶，休息到位！下一段就用舒服的节奏继续吧(☆▽☆)",
            "这次小休息很及时，给认真努力的你点赞～(ง •̀_•́)ง",
            "肩颈放松完成！不要把努力全憋在身体里嘛(✧∀✧)",
            "离开屏幕走两步，完成！现在的你看起来更有元气啦(๑´ڡ๑)",
            "休息好了就慢慢回来，今天的进度也能稳稳推进( •̀ᄇ• ́)ﻭ✧",
            "活动完成！让身体和心情一起换个清爽频道吧～(｡•ㅅ•｡)",
            "做到啦！给自己一点空隙，反而能走得更远(ﾉ◕ヮ◕)ﾉ*:･ﾟ✧",
            "这次休息很成功，像调好弦一样，接下来会更顺～(๑•̀ᗝ•́)૭",
            "走动和伸展完成！疲惫退场，元气返场(≧ω≦)/",
            "休息动作完成，辛苦啦！回来时也别忘了保持轻松(人 •͈ᴗ•͈)",
            "给眼睛和肩膀放个小假，完成得超棒！(๑˘︶˘๑)",
            "好状态重新加载完成，接下来一起把小目标唱完吧～(ﾉ´ヮ`)ﾉ*:･ﾟ✧",
            "休息到位啦，别急着冲，舒服地继续就好！(｡•̀ᴗ-)و ̑̑✧",
            "太好了，活动完成！今天也要元气满满地收尾哦～(≧◡≦)"
        ]);

    public static string PickCompletion(string reminderKind, Random? random = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderKind);

        var messages = ReminderKinds.IsWallClock(reminderKind)
            ? WaterCompletionMessages
            : ReminderKinds.IsActiveWork(reminderKind)
                ? ActivityCompletionMessages
                : throw new ArgumentException(
                    $"Unsupported reminder kind: {reminderKind}",
                    nameof(reminderKind));

        return messages[(random ?? Random.Shared).Next(messages.Count)];
    }
}

