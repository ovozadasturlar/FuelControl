namespace FuelControl.Contracts.Dto;

/// <summary>
/// POST /zaxira ("Hozir nusxa olish") javobi. Birinchi 3 maydon eskicha; qolganlari yangi (eski klient ularni e'tiborga olmaydi).
/// Sabab: "smena" | "qolda" (yangi nusxalar; "kunlik" - faqat eski fayllar). Papka — nusxa tushgan papka (to'liq yo'l). StandartPapkaga — sozlangan papkaga yozib bo'lmadi, nusxa
/// standart papkaga tushdi. TelegramYuboriladi — nusxadan keyin fonda Telegram'ga yuborish navbatga qo'yildi (natija GET /zaxira/holat da).
/// </summary>
public sealed record ZaxiraJavobiDto(string FaylNomi, long HajmBayt, DateTime Vaqt, string Sabab = "qolda", string Papka = "",
    bool StandartPapkaga = false, bool TelegramYuboriladi = false);

/// <summary>
/// Nusxa ro'yxati elementi. Vaqt — UTC (fayl nomidan). Sabab: "smena" | "qolda" | "kunlik" (eski fayllar) | "" (sababsiz eski nomlar); "smena" bo'lsa SmenaId bor.
/// StandartPapkada — fayl standart papkada (sozlangan papkada emas).
/// </summary>
public sealed record ZaxiraNusxasiDto(string Nomi, DateTime Vaqt, long HajmBayt, string Sabab, int? SmenaId, bool StandartPapkada);

/// <summary>
/// Telegram sozlamasi va oxirgi yuborish holati. Yoqilgan — foydalanuvchi belgisi; Faol = Yoqilgan + token bor + chat ID bor (faqat shunda yuboriladi).
/// TokenMaska — "…ab12" yoki null: token hech qachon to'liq qaytmaydi. OxirgiXabar — muvaffaqiyatda fayl va hajm, xatoda xato matni (tokensiz).
/// OxirgiTuri: "nusxa" | "sinov".
/// </summary>
public sealed record ZaxiraTelegramDto(bool Yoqilgan, bool Faol, string? TokenMaska, string? ChatId, DateTime? OxirgiVaqt, bool? OxirgiMuvaffaqiyat,
    string? OxirgiXabar, string? OxirgiTuri);

/// <summary>
/// GET /zaxira/holat. Papka — hozir nusxa tushayotgan papka; SozlanganPapka — Zaxira:Papka. StandartPapkaga = true bo'lsa sozlangan papka ishlamayapti
/// va StandartPapkaSababi to'ldiriladi; StandartPapkaSababi != null va StandartPapkaga = false — papka (standart bo'lsa ham) yozib bo'lmayapti (ogohlantirish).
/// Saqlash — nechta nusxa saqlanadi. Nusxalar — yangidan eskiga (sozlangan va standart papkadagilar birga).
/// </summary>
public sealed record ZaxiraHolatiDto(string Papka, string SozlanganPapka, bool StandartPapkaga, string? StandartPapkaSababi, int Saqlash,
    ZaxiraNusxasiDto[] Nusxalar, ZaxiraTelegramDto Telegram);

/// <summary>
/// PUT /zaxira/telegram. Har maydon: null — o'zgarmaydi. Token / ChatId "" — o'chiriladi, boshqa qiymat — o'rnatiladi
/// (token "123456789:AAH...", chat ID — son (guruhda manfiy) yoki @kanal). Noto'g'ri format — 400 (xato matnida token yo'q).
/// </summary>
public sealed record ZaxiraTelegramTahrirDto(string? Token = null, string? ChatId = null, bool? Yoqilgan = null);
