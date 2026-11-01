namespace FuelControl.Core.Modellar;

/// <summary>
/// Zaxira nusxa sozlamalari va Telegram holati: bazada bitta qator (Id = 1), birinchi saqlashda yaratiladi. Token ochiq saqlanmaydi:
/// <see cref="TelegramTokenShifr"/> — mashinaga bog'langan shifr (Windows'da DPAPI LocalMachine), shuning uchun nusxalar (E:\baza,
/// Telegram) boshqa kompyuterda tokenni ochib bermaydi.
/// </summary>
public sealed class ZaxiraSozlamasi
{
    public int Id { get; set; } = 1;

    /// <summary>Shifrlangan bot tokeni (versiya belgisi bilan, masalan "dpapi1:..."); yo'q bo'lsa null.</summary>
    public string? TelegramTokenShifr { get; set; }

    public string? TelegramChatId { get; set; }

    /// <summary>Foydalanuvchi belgisi. Yuboriladi faqat: Yoqilgan + token + chat ID.</summary>
    public bool TelegramYoqilgan { get; set; } = true;

    public DateTime? YuborishVaqti { get; set; }
    public bool? YuborishMuvaffaqiyat { get; set; }

    /// <summary>Muvaffaqiyatda fayl va hajm, xatoda xato matni (token maskalangan).</summary>
    public string? YuborishXabari { get; set; }

    /// <summary>"nusxa" | "sinov".</summary>
    public string? YuborishTuri { get; set; }

    /// <summary>Eng yangi qayta ishlangan (yuborilgan yoki hajmi katta bo'lgani uchun o'tkazib yuborilgan) nusxa nomi: undan yangilari "yuborilmagan".</summary>
    public string? OxirgiYuborilganFayl { get; set; }
}
