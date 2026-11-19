namespace Fishing;

enum ActionKind { None, Cast, Hook }
record Decision(ActionKind Action, string Status, bool Stop = false);
sealed class FishingLogic
{
    public int RecastDelay { get; set; } = 150;
    public int HookDelay { get; set; } = 80;
    public bool AutoCast { get; set; } = true;
    public bool Background { get; set; }
    public bool FilterEnabled { get; set; }
    public HashSet<int> AllowedItems { get; set; } = new();
    public bool Accepts(FishState s) => !FilterEnabled || (s.CatchIds ?? [s.CatchId]).All(id=>id>0 && AllowedItems.Contains(id));
    long lastClick, missingSince = -1, biteSince = -1;
    bool hookPending;
    int failedCasts;
    public void Reset(long now) { lastClick = now; missingSince = biteSince = -1; hookPending = false; failedCasts = 0; }
    public Decision Evaluate(FishState s, bool foreground, long now)
    {
        if ((!foreground && !Background) || s.Menu || s.Paused || s.Dead || s.Inventory || !s.HasRod)
        {
            missingSince = biteSince = -1;
            return new(ActionKind.None, !foreground && !Background ? "Пауза · переключитесь в Terraria" : s.Menu ? "Пауза · войдите в мир" : s.Dead ? "Пауза · персонаж погиб" : s.Inventory ? "Пауза · закройте интерфейс" : !s.HasRod ? "Пауза · возьмите удочку" : "Игра на паузе");
        }
        if (s.Bobbers > 0)
        {
            missingSince = -1; failedCasts = 0;
            if (hookPending) return now - lastClick > 6000 ? new(ActionKind.None, "Поплавок не вернулся после подсечки. Проверьте управление.", true) : new(ActionKind.None, "Возвращение поплавка…");
            if (s.Bite && !Accepts(s)) { biteSince = -1; return new(ActionKind.None, "Пропуск неподходящего улова"); }
            if (s.Bite && !s.Busy)
            {
                if (biteSince < 0) biteSince = now;
                if (now - biteSince >= HookDelay && now - lastClick >= 300) return new(ActionKind.Hook, "Подсечка");
                return new(ActionKind.None, "Поклёвка!");
            }
            biteSince = -1;
            return new(ActionKind.None, "Ожидание поклёвки");
        }
        hookPending = false; biteSince = -1;
        if (!AutoCast) return new(ActionKind.None, "Ожидание ручного заброса");
        if (s.Bait <= 0) return new(ActionKind.None, "Наживка закончилась", true);
        if (s.Busy) { missingSince = -1; return new(ActionKind.None, "Ожидание готовности удочки"); }
        if (missingSince < 0) missingSince = now;
        if (now - missingSince < RecastDelay || now - lastClick < 200) return new(ActionKind.None, "Подготовка к забросу…");
        if (failedCasts >= 3) return new(ActionKind.None, "Заброс не создаёт поплавок. Проверьте курсор и управление.", true);
        return new(ActionKind.Cast, "Заброс");
    }
    public void Clicked(ActionKind kind, long now)
    {
        lastClick = now; missingSince = biteSince = -1;
        if (kind == ActionKind.Hook) hookPending = true; else failedCasts++;
    }
}


