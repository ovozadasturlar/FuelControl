# FuelControl — Smena hisobi (yangi savdo modeli) topshirig'i

Sana: 2026-10-04. Muvofiqlashtiruvchi: **fc** sessiyasi. Ijrochilar: **fc-backend**, **fc-web**, **fc-desktop**.
Bu hujjat `docs/backend-topshiriq.md` va `docs/web-topshiriq.md` dagi **sotuv modelini almashtiradi**. Qolgan qoidalar (uslub, auth, i18n, portlar) o'z kuchida.

## 0. Manbalar

- **Dizayn (majburiy):** https://claude.ai/artifact/PpJUbfMwuZv3gajh9ws5ak — 17 ta artboard, Liquid Glass. Mahalliy nusxa: `docs/dizayn/*.dc.html` (oddiy HTML, inline style — rang, o'lcham, matnlarni shu yerdan oling).
  - `Main` — Savdo, ochiq smena · `SmenaYopish` — yopish jadvali (jonli hisob, formulani shu yerdagi skriptdan ham ko'ring) · `SmenaOchish` — ochish formasi
  - Dialoglar: `NasiyaQoshish`, `NasiyaQaytdi`, `XarajatQoshish`, `BakKirim`, `NarxOzgarishi`
  - Sahifalar: `Boshqaruv`, `Nasiyalar` (yangi), `Smenalar`, `Hisobotlar`, `Operatorlar`, `Audit`, `SozlamalarAparatlar`, `SozlamalarRuxsatlar`; `Menyu` — yon menyu
- Namuna raqamlar dizaynda bir-biriga mos (smena #39–#42). Seed/demo ma'lumotni shu raqamlarga yaqin qilsangiz, solishtirish oson.

## 1. Biznes qoidalari (mijoz bilan kelishilgan)

1. **Har bir sotuv kiritilmaydi.** Sotuv kiritish, offline navbat, sotuvni tahrirlash/bekor qilish — **butunlay olib tashlanadi**.
2. **Smena = bir sutka**, bitta operator hamma aparatlarga qaraydi → **bir vaqtda butun shoxobchada faqat bitta ochiq smena** (bazada ham kafolatlansin).
3. **Smena ochish** (faqat qo'lda): operator 3 ta qiymatni **qo'lda** yozadi, oldingi smenadan avtomatik **olinmaydi**:
   - `Qaytim` — kassada qaytim uchun qolgan naqd (50 000 / 100 000 kabi);
   - `Terminal` — terminal ekranidagi joriy summa (terminal 00:00 da o'zini nollaydi, ertalabki summa oldingi smenaga tegishli);
   - `Depozit` — zapravkaning umumiy bank kartasidagi qoldiq (kartasi yo'q xaridorlar ilova orqali shu kartaga tashlaydi).
4. **Smena davomida:**
   - **Nasiya** (bir nechta): mijoz ismi, telefon, mashina raqami, summa, muddat (sana), izoh. Muddati o'tganlari ajratib ko'rsatiladi.
   - **Qarz qaytdi**: to'liq yoki qisman; usul Naqd/Plastik/Depozit. Operator smena ichida yozsa — smena hisobiga **kirim** bo'ladi. Boshliq smenadan tashqari yopsa — smena hisobiga ta'sir qilmaydi.
   - **Xarajat** (bir nechta): summa, nima uchun, manba Kassa yoki Depozit karta. Boshliq kassadan naqd olib ketsa ham xarajat sifatida yoziladi.
   - Ochiq smenada xato yozilgan nasiya/xarajat/qaytishni muallif yoki boshliq o'chira oladi (audit bilan).
5. **Smena yopish:** har aparat uchun pultdagi umumiy litr ("Total, L"); yopishdagi terminal summasi (**bitta raqam** = tungi nollash chekidagi summa + nollashdan keyingi savdo); depozit karta qoldig'i; sanalgan naqd; izoh (ixtiyoriy).
6. **Formula** (server — yagona haqiqat manbai, klientlar jonli ko'rsatish uchun aynan shuni hisoblaydi):
   - har aparat segmenti: `Litr = Oxiri − Boshi` (2 xona), `Summa = round(Litr × Narx)` so'mgacha (MidpointRounding.AwayFromZero)
   - `Savdo = Σ Summa`, `JamiLitr = Σ Litr`
   - `Plastik = YopishTerminal − OchishTerminal`
   - `DepozitFarqi = YopishDepozit − OchishDepozit` (**manfiy bo'lishi mumkin**: tekshiruv paytida naqd olinib, depozit kartadan terminal orqali yechiladi — bu kamomat emas)
   - `NasiyaJami` = shu smenada yozilgan nasiyalar; `QaytganNasiya` = shu smena hisobiga yozilgan qaytishlar (har qanday usulda); `XarajatJami` = shu smenadagi barcha xarajatlar (Kassa **va** Depozit)
   - **`Kutilgan = Qaytim + Savdo + QaytganNasiya − Plastik − DepozitFarqi − NasiyaJami − XarajatJami`**
   - `Farq = SanalganNaqd − Kutilgan`. Manfiy → operator hisobiga `Kamomat` harakati (oylikdan ayiriladi). Musbat → `Ortiqcha` harakati. Izoh: `Smena #N`.
   - Tekshiruv: dizayndagi namuna — savdo 16 468 520, plastik 7 830 000 − 200 000, depozit 3 410 000 − 1 250 000, nasiya 570 000, qaytgan 300 000, xarajat 1 585 000, qaytim 100 000 → kutilgan **4 923 520**; sanalgan 4 878 000 → kamomat **45 520**. Bu holat Core testida bo'lsin.
7. **Validatsiya:** yangi pult ko'rsatkichi oldingisidan **kichik bo'lsa saqlanmaydi** (faqat aparatlar uchun; terminal va depozit kamayishi mumkin). Barcha faol aparatlar ko'rsatkichi majburiy. Pul maydonlari ≥ 0.
8. **Yopilgandan keyin:** aparat `TotalLitr` = yangi ko'rsatkich; aparat bakidan sotilgan litr ayiriladi.
9. **Bak:** har aparatning o'z baki. Kirim **litrda** (zavoddan kelgan). Qoldiq = kirimlar − sotilgan. Aparat yaratishda boshlang'ich bak qoldig'i kiritiladi. Qo'lda tuzatish faqat Sozlamalarda, sabab bilan, auditga.
10. **Narx o'zgarishi ochiq smenada:** o'sha yoqilg'i aparatlarining hozirgi pult ko'rsatkichi **majburiy so'raladi**; shu paytgacha bo'lgan litr eski narxda alohida segment bo'lib yoziladi, qolgani yopishda yangi narxda. (Smena yopilganda bitta aparatda 2 segment bo'lishi mumkin.)
11. **Ko'rsatkichni tuzatish** (sotuv tahririning o'rnini egallaydi): faqat `KorsatkichTuzatish` ruxsati bilan, **faqat oxirgi yopilgan smena** uchun, sabab majburiy. Smena qayta hisoblanadi, kamomat/ortiqcha farqi uchun tuzatuvchi harakat yoziladi, bak va `TotalLitr` delta bo'yicha tuzatiladi, keyingi (ochiq) smenaning boshlang'ich ko'rsatkichi ham yangilanadi. Auditga eski/yangi qiymat.
12. **Kunlik hisobot:** smena ochilgan sanaga yoziladi.
13. **Nomlash:** "Click" hamma joyda **"Depozit"**. Terminal = "Plastik".
14. Pul — `long` so'm, litr — `decimal(18,2)`, vaqt bazada UTC, ko'rsatishda Toshkent.

## 2. Ruxsatlar

`enum Ruxsat`: `Boshqaruv, Savdo, SmenaOchish, SmenaYopish, Smenalar, Nasiyalar, NasiyaYozish, QarzQaytdi, XarajatYozish, BakKirim, KorsatkichTuzatish, Hisobotlar, Eksport, Operatorlar, AvansBerish, Audit, Sozlamalar`.
Olib tashlanadi: `SotuvKiritish` (→ `Savdo`), `SotuvTahrirlash`, `SotuvBekorQilish`.
Standart: **Operator** = Savdo, Nasiyalar, SmenaOchish, SmenaYopish, NasiyaYozish, QarzQaytdi, XarajatYozish. **Boshliq** = hammasi, Sozlamalar'dan tashqari. **Admin** = hammasi.
Bazadagi mavjud foydalanuvchilar ruxsatlari migratsiyada moslashtirilsin (SotuvKiritish → Savdo; operatorlarga yangi standartlar qo'shilsin).

## 3. Contracts (shartnoma — nomlar va shakl **majburiy**)

Faqat fc-backend o'zgartiradi. Qo'shimcha maydon qo'shish mumkin (oxiriga), nomini o'zgartirish yoki olib tashlash — faqat fc orqali kelishib.

```csharp
public enum TolovTuri { Naqd, Plastik, Depozit }          // Click → Depozit
public enum XarajatManbai { Kassa, Depozit }
public enum NasiyaHolati { Faol, MuddatiOtgan, Yopilgan }
public enum HisobotGuruhi { Smena, Kun, Oy, Operator }
// SotuvHolati — olib tashlanadi. HarakatTuri — o'zgarmaydi.

// --- Aparat / bak ---
public sealed record AparatDto(int Id, int Raqam, int YoqilgiTuriId, string YoqilgiNomi, decimal TotalLitr,
    decimal BakQoldiq, DateTime? OxirgiKirimVaqti, decimal? OxirgiKirimLitr);
public sealed record AparatYaratishDto(int Raqam, int YoqilgiTuriId, decimal BoshlangichTotalLitr, decimal BoshlangichBakQoldiq);
public sealed record AparatTahrirlashDto(int Raqam, int YoqilgiTuriId, decimal? TotalLitr = null, decimal? BakQoldiq = null, string? Sabab = null);
public sealed record BakKirimYaratishDto(decimal Litr, DateTime? Vaqt, string? Hujjat);
public sealed record BakKirimDto(int Id, int AparatId, int AparatRaqam, decimal Litr, decimal QoldiqOldin, decimal QoldiqKeyin,
    DateTime Vaqt, string? Hujjat, string KimYozdi);

// --- Yoqilg'i (narx ochiq smenada o'zgarsa Korsatkichlar majburiy) ---
public sealed record YoqilgiTahrirlashDto(string Nomi, long Narx, string Rang, AparatKorsatkichDto[]? Korsatkichlar = null);

// --- Smena ---
public sealed record SmenaDto(
    int Id, int OperatorId, string OperatorIsmi, DateTime Boshlandi, DateTime? Tugadi,
    long OchishQaytim, long OchishTerminal, long OchishDepozit,
    long? YopishTerminal, long? YopishDepozit, long? SanalganNaqd,
    decimal JamiLitr, long Savdo, long Plastik, long DepozitFarqi,
    long NasiyaJami, long QaytganNasiya, long XarajatJami,
    long Kutilgan, long Farq, string? Izoh);
// Ochiq smenada: JamiLitr/Savdo/Plastik/DepozitFarqi/Kutilgan/Farq = 0; NasiyaJami/QaytganNasiya/XarajatJami — joriy yig'indilar.

public sealed record SmenaKorsatkichDto(int AparatId, int AparatRaqam, string YoqilgiNomi,
    decimal Boshi, decimal Oxiri, long Narx, decimal Litr, long Summa, bool NarxOzgarishida);
public sealed record SmenaTafsilotDto(SmenaDto Smena, SmenaKorsatkichDto[] Korsatkichlar,
    NasiyaDto[] Nasiyalar, NasiyaQaytishiDto[] Qaytishlar, XarajatDto[] Xarajatlar);
public sealed record SmenaQisqaDto(int Id, DateOnly Sana, string OperatorIsmi, long Savdo, decimal Litr, long Farq);

public sealed record SmenaOchishDto(long Qaytim, long Terminal, long Depozit);
public sealed record AparatKorsatkichDto(int AparatId, decimal Qiymat);
public sealed record SmenaYopishDto(AparatKorsatkichDto[] Korsatkichlar, long Terminal, long Depozit, long SanalganNaqd, string? Izoh);
public sealed record KorsatkichTuzatishDto(int AparatId, decimal Qiymat, string Sabab);

// --- Nasiya ---
public sealed record NasiyaDto(int Id, int SmenaId, string OperatorIsmi, string MijozIsmi, string Telefon, string MashinaRaqami,
    long Summa, long Qaytgan, long Qoldiq, DateOnly Muddat, NasiyaHolati Holati, int MuddatgachaKun,
    DateTime Yozildi, DateTime? Yopildi, string? Izoh);          // MuddatgachaKun: Muddat − bugun (Toshkent), manfiy = o'tgan
public sealed record NasiyaYaratishDto(string MijozIsmi, string Telefon, string MashinaRaqami, long Summa, DateOnly Muddat, string? Izoh);
public sealed record NasiyaQaytishiYaratishDto(long Summa, TolovTuri Usul, bool SmenaHisobiga, string? Izoh);
public sealed record NasiyaQaytishiDto(int Id, int NasiyaId, string MijozIsmi, int? SmenaId, long Summa, TolovTuri Usul,
    DateTime Vaqt, string KimYozdi, string? Izoh);
public sealed record NasiyaTafsilotDto(NasiyaDto Nasiya, NasiyaQaytishiDto[] Qaytishlar);
public sealed record NasiyalarXulosaDto(long FaolQarz, int FaolSoni, long MuddatiOtgan, int MuddatiOtganSoni,
    long OyBerilgan, int OyBerilganSoni, long OyQaytgan, int OyQaytganSoni);
public sealed record NasiyalarDto(NasiyalarXulosaDto Xulosa, NasiyaDto[] Royxat);

// --- Xarajat ---
public sealed record XarajatDto(int Id, int SmenaId, long Summa, string Sabab, XarajatManbai Manba, DateTime Vaqt, string KimYozdi);
public sealed record XarajatYaratishDto(long Summa, string Sabab, XarajatManbai Manba);

// --- Hisobot ---
public sealed record HisobotQatoriDto(string Guruh, DateOnly? Sana, string? OperatorIsmi, int SmenaSoni, decimal Litr,
    long Savdo, long Plastik, long Depozit, long Nasiya, long QaytganNasiya, long Xarajat, long NaqdSavdo,
    long Kamomat, long Ortiqcha, bool Jami);
// Guruh tilga bog'liq emas: smena → "41", kun → "yyyy-MM-dd", oy → "yyyy-MM", operator → ism.
// NaqdSavdo = Savdo − Plastik − Depozit(farqi) − Nasiya.
public sealed record HisobotAparatDto(int AparatId, int Raqam, string YoqilgiNomi,
    decimal BakBoshida, decimal Kirim, decimal Sotildi, decimal BakOxirida, long Savdo);
public sealed record HisobotDto(HisobotQatoriDto[] Qatorlar, HisobotQatoriDto Jami, HisobotAparatDto[] Aparatlar, long Avans);

// --- Boshqaruv ---
public sealed record TolovTaqsimotiDto(long Naqd, long Plastik, long Depozit, long Nasiya);
public sealed record BoshqaruvDto(SmenaDto? JoriySmena, SmenaDto? OxirgiYopilgan,
    long OySavdo, decimal OyLitr, int OySmenaSoni, long OyKamomat, long OyOrtiqcha,
    NasiyalarXulosaDto Nasiyalar, AparatDto[] Aparatlar,
    SmenaQisqaDto[] OxirgiSmenalar,        // 14 ta yopilgan, eskisidan yangisiga
    TolovTaqsimotiDto OyTolovlar, SmenaDto[] OxirgiYopilganlar);   // 3 ta

// --- Audit (additiv) ---
public sealed record AuditYozuviDto(int Id, DateTime Vaqt, string Kim, string Amal, string Tafsilot, string Tur);
// Tur: "smena" | "nasiya" | "xarajat" | "bak" | "tuzatish" | "hisob" | "sozlama" | "kirish"
```

Olib tashlanadi: `SotuvDto`, `SotuvYaratishDto`, `SotuvTahrirlashDto`, `SotuvBekorQilishDto`, eski `SmenaYopishDto`/`SmenaDto` shakli, `BoshqaruvBugunDto` va uning qismlari.

## 4. API

| Metod va yo'l | Tana / javob | Ruxsat va qoidalar |
|---|---|---|
| `GET /smenalar?dan&gacha&operatorId` | `SmenaDto[]` | Smenalar; operator faqat o'zinikini ko'radi |
| `GET /smenalar/joriy` | `SmenaTafsilotDto` yoki 204 | ochiq smena (butun shoxobcha bo'yicha) |
| `GET /smenalar/{id}` | `SmenaTafsilotDto` | Smenalar yoki o'z smenasi |
| `POST /smenalar/och` | `SmenaOchishDto` → `SmenaDto` | SmenaOchish; ochiq smena bo'lsa 409 |
| `POST /smenalar/{id}/yop` | `SmenaYopishDto` → `SmenaDto` | SmenaYopish; operator faqat o'zinikini; §1.6–1.8 |
| `PUT /smenalar/{id}/korsatkich` | `KorsatkichTuzatishDto` → `SmenaTafsilotDto` | KorsatkichTuzatish; §1.11 |
| `GET /nasiyalar?holat=faol\|otgan\|yopilgan&q=` | `NasiyalarDto` | Nasiyalar; `q` — ism/telefon/raqam |
| `GET /nasiyalar/{id}` | `NasiyaTafsilotDto` | Nasiyalar |
| `POST /nasiyalar` | `NasiyaYaratishDto` → `NasiyaDto` | NasiyaYozish; ochiq smena majburiy |
| `POST /nasiyalar/{id}/qaytish` | `NasiyaQaytishiYaratishDto` → `NasiyaDto` | QarzQaytdi; summa ≤ qoldiq; `SmenaHisobiga=true` bo'lsa ochiq smena majburiy |
| `DELETE /nasiyalar/{id}`, `DELETE /nasiyalar/qaytishlar/{id}` | 204 | faqat ochiq smenada, muallif yoki boshliq |
| `GET /xarajatlar?smenaId&dan&gacha` | `XarajatDto[]` | Smenalar yoki o'z smenasi |
| `POST /xarajatlar` | `XarajatYaratishDto` → `XarajatDto` | XarajatYozish; ochiq smena majburiy |
| `DELETE /xarajatlar/{id}` | 204 | faqat ochiq smenada, muallif yoki boshliq |
| `GET /aparatlar` | `AparatDto[]` | kirgan har kim |
| `POST /aparatlar/{id}/kirim` | `BakKirimYaratishDto` → `AparatDto` | BakKirim |
| `GET /bak-kirimlar?aparatId&dan&gacha` | `BakKirimDto[]` | Hisobotlar yoki BakKirim |
| `PUT /aparatlar/{id}` | `AparatTahrirlashDto` | Sozlamalar; TotalLitr/BakQoldiq o'zgarsa `Sabab` majburiy |
| `PUT /yoqilgilar/{id}` | `YoqilgiTahrirlashDto` | Sozlamalar; ochiq smenada narx o'zgarsa, o'sha yoqilg'i aparatlari ko'rsatkichi bo'lmasa 400 (ProblemDetails `extensions.kerakliAparatlar: int[]`) |
| `GET /hisobot?dan&gacha&operatorId&guruh=smena\|kun\|oy\|operator` | `HisobotDto` | Hisobotlar; faqat yopilgan smenalar |
| `GET /boshqaruv` | `BoshqaruvDto` | Boshqaruv (`/boshqaruv/bugun` olib tashlanadi) |
| `GET /audit?q&tur&dan&gacha` | `AuditYozuviDto[]` | Audit |

Olib tashlanadi: barcha `/sotuvlar` yo'llari. `/operatorlar/{id}/hisob` saqlanadi, oylik savdo endi yopilgan smenalardan.
**SignalR** `/hub`: `SmenaOzgardi`, `NasiyaOzgardi`, `XarajatOzgardi`, `AparatOzgardi`, `NarxOzgardi` (+ mavjud `QaytaUlan`). `SotuvQoshildi`/`SotuvOzgardi` olib tashlanadi.
**Audit** amallari (tur bilan): Smena ochildi/yopildi, Nasiya yozildi/o'chirildi, Qarz qaytdi, Xarajat yozildi/o'chirildi, Bakka kirim, Ko'rsatkich tuzatildi, Narx o'zgardi, Aparat tahrirlandi va mavjudlari.

## 5. Ma'lumotlar bazasi

- Yangi jadvallar: `SmenaKorsatkichlari` (SmenaId, AparatId, Tartib, Boshi, Oxiri, Narx, Litr, Summa, NarxOzgarishida, Vaqt), `Nasiyalar`, `NasiyaQaytishlari`, `Xarajatlar`, `BakKirimlari`. `Aparat` + `BakQoldiq`. `Smena` — §3 dagi maydonlar. `AuditYozuvi` + `Tur`.
- `Sotuvlar`/`SotuvTolovlari` va eski smena ustunlari olib tashlanadi. **Yangi migratsiya** (eskilarini tahrir qilmang): o'rnatuvchi 0.0.1 bazasidan ham to'g'ri o'tsin. Dev bazani qayta yaratish mumkin.
- Bitta ochiq smena — bazada ham kafolat (masalan, ifodali partial unique index).
- Seed (Development, DemoMalumot): 2 operator, 5 aparat va bak qoldiqlari, bir nechta yopilgan smena, nasiyalar (muddati o'tgan bilan), xarajat va kirimlar — dizayndagi raqamlarga yaqin.

## 6. Vazifalar

### fc-backend
1. **B1 — shartnoma:** `Contracts` (§3), `Ruxsat` enum, endpoint imzolari. Tugashi bilan fc'ga qisqa xabar — web va desktop shundan generatsiya qiladi.
2. **B2 — Core:** modellari va `SmenaHisoblagich` (formula), segmentlar, bak, tuzatish. **Testlar:** §1.6 namunasi; tekshiruv holati (depozit manfiy); depozitdan xarajat (kassaga ta'siri 0); qaytishning 3 usuli; narx o'zgarishi bilan 2 segment; kichik ko'rsatkich rad etiladi; tuzatishdan keyin kamomat farqi harakati.
3. **B3 — Api:** migratsiya, endpointlar, validatsiya, audit, SignalR, seed. **Testlar:** ochish/yopish, ikkinchi ochiq smena 409, ruxsatlar 403, nasiya muddati o'tgani, kirim, narx o'zgarishi 400 va to'g'ri holat, tuzatish, hisobot va boshqaruv yig'indilari.
4. `dotnet build` va `dotnet test` xatosiz (`--artifacts-path` scratchpad'da). API :5000 da ishga tushib, `/openapi/v1.json` yangi shakl bilan ochilsin.

### fc-web (Angular PWA)
1. Backend B1 tayyor bo'lguncha: dizayn bo'yicha UI'ni mock ma'lumot bilan quring; sotuv sahifasi, offline navbat (IndexedDB) va sotuv tahririni olib tashlang; "Click" → "Depozit".
2. Ekranlar (dizayndagidek): Savdo (ochiq smena / ochish formasi / **yopish jadvali — formulani jonli hisoblaydi, server natijasi bilan bir xil**), dialoglar (nasiya, qarz qaytdi, xarajat, bakka kirim, narx o'zgarishi), **Nasiyalar** (yangi), Boshqaruv, Smenalar (ko'rsatkichlar va pul hisobi bilan tafsilot, boshliq uchun tuzatish), Hisobotlar (yangi ustunlar + aparat/bak jadvali + Excel), Operatorlar, Audit (tur filtri), Sozlamalar (aparat bak maydoni, narx o'zgarishi ko'rsatkichlar bilan, yangi ruxsatlar).
3. **Telefon (390 px):** yopish jadvali har aparat uchun karta bo'lib chiqsin, jadval emas. Pastki tab-bar ruxsatga qarab.
4. B1 dan keyin `npm run api` bilan klientni qayta generatsiya qiling va ulang. SignalR yangi hodisalar.
5. Yangi lug'at kalitlari `lugat-web.json` ga (3 til). Kalit nomlarini desktop bilan bir xil qiling — keyin fc-desktop `Til.cs` ga ko'chiradi.
6. `ng build` xatosiz, 390×844 va 1440 da skrinshot bilan tekshirish (headless Edge).

### fc-desktop (Avalonia) — sessiya ochilgach
1. Backend B1 dan keyin `Contracts` o'zgaradi — desktop build vaqtincha buzilishi kutiladi; B1 xabaridan keyin moslang.
2. `SotuvView` → yangi Savdo (ochiq/yopiq holat, ochish formasi), yopish ko'rinishi, dialoglar, **NasiyalarView** + menyu bandi, Boshqaruv/Smenalar/Hisobot/Operatorlar/Audit/Sozlamalar yangilash, `SotuvTahrirViewModel` va sotuv dialoglarini olib tashlash, `Til.cs` yangi kalitlar (3 til), Excel eksport yangi hisobot shaklida, `Malumot.cs` kesh va SignalR yangi hodisalar.
3. Tekshirish: Avalonia.Headless harness skrinshotlari (oldingidek), `%AppData%` va `Documents` ni keyin tozalang.

## 7. B1 dan keyin kelishilgan aniqliklar (2026-10-04, majburiy)

1. **"Oldingi" ko'rsatkich** (yopish jadvali va narx o'zgarishi dialogi): aparatda ochiq smenada qayd etilgan segment bo'lsa — oxirgisining `Oxiri`, aks holda `AparatDto.TotalLitr`. `TotalLitr` va bak qoldig'i faqat smena yopilganda o'zgaradi.
2. **Ochiq smenadagi segmentlar:** `SmenaTafsilotDto.Korsatkichlar` ochiq smenada faqat narx o'zgarishida qayd etilgan segmentlarni beradi (`NarxOzgarishida = true`). Klient jonli savdoni shunday hisoblaydi: qayd etilgan segmentlar `Summa`/`Litr` yig'indisi + har aparat uchun `(yangi − oldingi) × joriy narx` (`YoqilgiTuriDto.Narx`). Ochiq smenada `SmenaDto.Savdo` = 0. Yopilgan smenada `Korsatkichlar` — barcha segmentlar.
3. **SignalR:** `SmenaOzgardi`, `NasiyaOzgardi`, `XarajatOzgardi`, `AparatOzgardi`, `NarxOzgardi` yuki — o'zgargan yozuv DTO'si. Klient buni **"qayta yuklash" signali** deb biladi (smena uchun `GET /smenalar/joriy`), DTO'ni ro'yxatga o'zi qo'shmaydi. O'chirishda ham shu hodisa keladi.
4. **"Boshliq" API'da** = `Smenalar` ruxsati bor foydalanuvchi: boshqalar yozgan nasiya/qaytish/xarajatni o'chira oladi va boshqa operator smenasini yopa oladi. Qolganlar faqat o'z yozuvini.
5. **O'chirish:** qaytishi bor nasiya o'chirilmaydi (409, avval qaytishlar o'chiriladi). Qaytish faqat uning smenasi ochiq bo'lsa o'chiriladi; smenaga bog'lanmagan qaytishni faqat boshliq o'chiradi.
6. **Hisobot:** `guruh` berilmasa = smena; noma'lum qiymat 400. `Sana` — smena va kun qatorlarida, oy/operator qatorlarida null. `OperatorIsmi` — faqat smena qatorlarida. Boshqaruv va hisobot faqat yopilgan smenalardan.
7. **Nasiyalar xulosasi:** `FaolQarz`/`FaolSoni` qoldig'i bor barcha nasiyalar, muddati o'tganlar ham kiradi. `Oy*` — joriy Toshkent oyi.
8. `GET /smenalar/joriy` va `GET /aparatlar` — kirgan har kim; `GET /bak-kirimlar` — Hisobotlar yoki BakKirim.
9. **Segmentlar:** narx o'zgarishida litr 0 bo'lsa segment yozilmaydi; yopishda har aparatga kamida bitta qator (litr 0 bo'lsa ham). `NarxOzgarishida = true` faqat narx o'zgarishida qayd etilgan eski narxdagi segmentda.
10. **Nasiya validatsiyasi:** mijoz ismi, summa > 0 va muddat majburiy; telefon yoki mashina raqamidan **kamida bittasi** majburiy; muddat yozilayotgan kundan oldin bo'lmaydi. `holat=faol` — qarzi bor hammasi (muddati o'tganlar ham), `otgan` — faqat muddati o'tganlar, `yopilgan` — qoldig'i 0; parametr berilmasa — hammasi.
11. **Bak manfiy:** smena yopilganda bak qoldig'i manfiyga tushsa ham smena yopiladi (kirim yozilmay qolgan bo'lishi mumkin) — UI manfiy qoldiqni qizil ko'rsatadi. Qo'lda tuzatishda manfiy qiymat taqiqlanadi.
12. **Hisobotdagi bak jadvali:** Sotildi va Savdo — davrdagi yopilgan smenalardan; BakOxirida — davr oxiridagi qoldiq; BakBoshida = BakOxirida − Kirim + Sotildi. Operator filtri bak jadvaliga ta'sir qilmaydi, faqat qatorlar va Jami'ga.
13. **Shartnomaga additiv qo'shimchalar (2026-10-05):** `NasiyaDto`, `NasiyaQaytishiDto`, `XarajatDto` oxiriga `int MuallifId` qo'shiladi, klient "o'z yozuvi"ni ism bo'yicha emas, shu bo'yicha tekshiradi. `HisobotQatoriDto` oxiriga `int XarajatSoni` qo'shiladi. Yangi endpoint `GET /smenalar/oxirgi` oxirgi yopilgan smenani `SmenaTafsilotDto` ko'rinishida qaytaradi yoki 204 beradi, ruxsati `/smenalar/joriy` bilan bir xil.
14. **Ruxsatlar sahifasi:** o'zgarishlar avval qoralama bo'ladi, faqat "Ruxsatlarni saqlash" tugmasi bilan yuboriladi.
15. **Dev muhit:** demo ma'lumot `Seed:DemoMalumot=true` bo'lsa Development **va** Web muhitida, faqat bo'sh bazada yoziladi. Dev bazani (:5000) faqat fc qayta yaratadi. Web o'zining :5100 dagi bazasida, desktop :5000 da sinaydi. Bir vaqtda faqat bitta ochiq smena bo'lgani uchun ular bir bazada sinamaydi.
16. **Demo ma'lumot dizayn bilan aynan mos bo'ladi:** har smena qatori `Hisobotlar.dc.html` dagi kabi, har aparat litri `Hisobotlar.dc.html` va `Smenalar.dc.html` dagi kabi. Nasiyalar `Nasiyalar.dc.html` dagi smenalarga bog'lanadi. Barcha yopilgan demo smenalarda aparat segmentlari bor va bak tarixi izchil.

## 8. Qo'shimchalar (2026-10-06, foydalanuvchi sinovidan keyin)

1. **Telefon formati hamma joyda `+998 XX XXX XX XX`.**
   - Kiritishda maydon `+998 ` bilan boshlanadi va bu prefiksni o'chirib bo'lmaydi. Foydalanuvchi faqat 9 raqam yozadi, bo'shliqlar yozish paytida o'zi qo'yiladi.
   - Istalgan ko'rinishdagi qiymatni joylashtirish ham ishlaydi: raqam bo'lmagan belgilar olib tashlanadi, boshidagi `998` tushiriladi.
   - Ko'rsatishda ham hamma joyda shu format.
   - Server telefonni saqlashdan oldin normallashtiradi va doim `+998 XX XXX XX XX` ko'rinishida qaytaradi. Telefon berilgan bo'lsa, `998` dan keyin aynan 9 raqam bo'lishi shart, aks holda 400. Telefon yoki mashina raqamidan bittasi majburiyligi o'zgarmaydi. Bazadagi mavjud telefonlar migratsiyada shu formatga keltiriladi.
2. **Mavjud mijozni topish va avtomatik to'ldirish.**
   - Yangi endpoint: `GET /nasiyalar/mijozlar?q=` → `MijozTaklifDto[]`, eng ko'pi 8 ta. Ruxsat: `NasiyaYozish`, `QarzQaytdi` yoki `Nasiyalar`.
   - `public sealed record MijozTaklifDto(string MijozIsmi, string Telefon, string MashinaRaqami, int NasiyaSoni, long FaolQarz, DateOnly OxirgiNasiya);`
   - Mijozlar nasiya yozuvlaridan yig'iladi. Telefon bo'lsa telefon bo'yicha, bo'lmasa ism va mashina raqami bo'yicha guruhlanadi.
   - Nasiya yozish dialogida ism, telefon yoki mashina raqami yozilayotganda takliflar ro'yxati chiqadi. Har bir taklifda ism, telefon, raqam va faol qarz ko'rinadi. Tanlansa, uchala maydon to'ladi.
   - Telefon to'liq kiritilib, mavjud mijozga to'g'ri kelsa, bo'sh maydonlar o'zi to'ladi va "Mavjud mijoz · faol qarz X" izohi chiqadi.
3. **Qidiruv qoidasi** `GET /nasiyalar?q=` va `/nasiyalar/mijozlar?q=` uchun bir xil:
   - **Ism.** Katta-kichik harf farqlanmaydi. Apostroflar (`'`, `‘`, `’`, `` ` ``, `ʻ`, `ʼ`, `´`) solishtirishda **butunlay tashlab yuboriladi**, shuning uchun "Toxtayev", "To'xtayev" va "TO`XTAYEV" bir-birini topadi. So'z boshi ham, ichidagi qism ham mos keladi, shuning uchun o'xshash ismlar chiqadi.
   - **Telefon.** Faqat raqamlar solishtiriladi, `+998`, bo'shliq va chiziqlar e'tiborga olinmaydi. Kamida 3 raqam yozilsa qidiradi.
   - **Mashina raqami.** Bo'shliqlarsiz va katta harfda solishtiriladi.
   - **Tartib.** Avval aynan va boshidan mos kelganlar, keyin qolganlari, yangisi oldinda.
   - **Qarz qaytdi.** Dialogdagi qidiruv ham shu qoidaga bo'ysunadi, telefon raqami bilan ham topiladi.
4. **Enter bilan keyingi maydonga o'tish.**
   - Smena yopishda N-aparat ko'rsatkichida Enter bosilsa, fokus N+1-aparatga o'tadi. Oxirgi aparatdan keyin tartib shunday: terminal, depozit, sanalgan naqd, "Smenani yopish" tugmasi.
   - Tugma Enter bilan o'zi bosilmaydi, faqat fokus oladi.
   - Smena ochish formasi va narx o'zgarishi dialogida ham shu tartib ishlaydi.
5. **Web yangilanishi.** Yangi versiya chiqqanda PWA eski keshlangan nusxada qolib ketmasligi kerak. Yangilanish aniqlansa, "Yangi versiya" xabari chiqadi va bir bosishda sahifa qayta yuklanadi, yoki yangi versiya avtomatik faollashadi.

6. **Nasiyalar → Excel (2026-10-08).** Nasiyalar sahifasida "Excel" tugmasi bo'ladi, faqat `Eksport` ruxsati bilan. U joriy filtr va qidiruv bo'yicha ko'rinib turgan ro'yxatni chiqaradi.
   - Ustunlar: Mijoz, Telefon, Mashina raqami, Yozilgan sana, Smena, Operator, Qarz, Qaytgan, Qoldiq, Muddat, Holat (masalan "7 kun o'tdi", "Yopilgan").
   - Oxirida Jami qatori bo'ladi: Qarz, Qaytgan, Qoldiq yig'indilari.
   - Fayl nomi `nasiyalar-YYYY-MM-DD.xlsx`. Eksport `POST /audit/eksport` orqali auditga yoziladi, xuddi boshqa eksportlar kabi.
7. **Jami qarzdorlik ko'rinsin (2026-10-08).**
   - **Nasiyalar sahifasi.** "Faol qarz" KPI nomi **"Jami qarzdorlik"** bo'ladi. Izohi: "7 ta mijoz · shundan muddati o'tgan 480 000". Jadval oxirida joriy filtr bo'yicha **Jami** qatori bo'ladi: Qarz, Qaytgan, Qoldiq.
   - **Savdo sahifasi.** "Muddati o'tgan qarzlar" kartasining tepasida "Jami qarzdorlik 2 510 000 · 7 ta mijoz" qatori chiqadi. Muddati o'tganlar ro'yxati uning ostida qoladi.
   - **Boshqaruv.** Nasiya KPI plitkasida asosiy raqam jami qarzdorlik bo'ladi. Ostida qizil rangda "muddati o'tgan 480 000 · 2 ta" yoziladi. Plitka nomi "Nasiya qarzdorligi".
   - Barcha raqamlar `NasiyalarXulosaDto` dan olinadi: `FaolQarz`, `FaolSoni`, `MuddatiOtgan`, `MuddatiOtganSoni`. Jami qatori esa klientda joriy ro'yxatdan hisoblanadi.
8. **Nasiya yozish: Enter bilan maydondan maydonga o'tish (2026-10-10).** Desktop va web'da bir xil ishlaydi.
   - **Mijoz ismi** maydonida Enter bosilganda:
     - takliflar ro'yxati ochiq bo'lsa va unda element tanlangan bo'lsa, o'sha mijoz tanlanadi (ism, telefon va mashina raqami to'ladi), fokus esa birinchi bo'sh maydonga, odatda Summa'ga o'tadi;
     - taklif yo'q bo'lsa (yangi mijoz) yoki ro'yxat yopiq bo'lsa, fokus **Telefon** maydoniga o'tadi. Ism bo'sh bo'lsa, fokus joyida qoladi.
   - Keyingi tartib: Telefon → Mashina raqami → Summa → Muddat → Izoh → "Saqlash" tugmasi. Tugma Enter bilan o'zi bosilmaydi, faqat fokus oladi (8.4 bilan bir xil).
   - Takliflar ro'yxati ochiq bo'lsa, ↑/↓ unda yuradi, Esc uni yopadi va matn o'zgarmaydi.
9. **Plastik bir nechta summa bilan kiritiladi (2026-10-10).** Plastik summasi bir necha joydan olinadi: terminaldagi savdo, kassa aparatidagi summa, kechasi 00:00 da avtomatik chiqadigan nollash cheki. Shuning uchun yopishda bitta maydon emas, ro'yxat bo'ladi. Desktop va web'da bir xil ishlaydi.
   - **Ochishda** terminal summasi o'zgarishsiz bitta raqam bo'lib qoladi (`OchishTerminal`).
   - **Yopishda** "Plastik" bo'limida summalar ro'yxati bo'ladi:
     - boshida bitta bo'sh qator turadi;
     - "+ Plastik qo'shish" yangi qator qo'shadi va unga fokus beradi;
     - har qatorda faqat summa va o'chirish (×) tugmasi bo'ladi, qatorga nom yoki "qayerdan" yozilmaydi;
     - bo'sh qatorlar hisobga olinmaydi, kamida bitta qiymat kerak (0 ham mumkin);
     - manfiy summa mumkin emas.
   - **Hisob:** jami plastik = qatorlar yig'indisi. Shu smena plastigi = jami − `OchishTerminal`. Kassa formulasi (1-bo'lim) o'zgarmaydi. Ekranda "Jami plastik" va "Shu smena plastik" ko'rinadi.
   - **Enter tartibi (8.4):** aparatlar → plastik qatorlari ketma-ket → depozit → sanalgan naqd → "Smenani yopish" tugmasi.
   - **Shartnoma** (qo'shimcha, eski klientlar buzilmaydi):
     - `SmenaYopishDto` oxiriga `long[]? PlastikSummalari = null` qo'shiladi. Berilsa, har biri ≥ 0, ko'pi bilan 20 ta, va `Terminal` ularning yig'indisiga teng bo'lishi shart, aks holda 400. Berilmasa, eski xatti-harakat saqlanadi.
     - Server summalarni smenada saqlaydi; migratsiya qo'shimcha ustun bilan.
     - `SmenaDto` oxiriga `long[] PlastikSummalari` qo'shiladi; eski smenalarda bo'sh massiv.
   - **Ko'rinishi:** Smenalar tafsiloti va smena Excel eksportida plastik qismlari bilan chiqadi, masalan "7 830 000 + 300 000 + 150 000 = 8 280 000; ochishda 200 000 → shu smena 8 080 000". Eski smenalarda bitta raqam ko'rinadi.
10. **Savdo (smena yopiq): "Oxirgi smena" kartasi (2026-10-10).** Keyingi operator oldingi operatorning pul natijasini ko'rmasligi kerak.
   - **Natija qismi** (savdo, litr, plastik, depozit farqi, nasiya, xarajat, kamomat yoki ortiqcha) faqat ikki kishiga ko'rinadi: boshliqqa (`Smenalar` ruxsati) va shu smenaning operatoriga (`OperatorId` = joriy foydalanuvchi).
   - **Qolganlar**, ya'ni keyingi operator, faqat "Oldingi smena" topshirish blokini ko'radi:
     - "#N · operator · DD.MM HH:mm da yopildi";
     - "Yopilgandagi depozit karta qoldig'i: X".
     Qiymat faqat ma'lumot uchun. U ochish formasiga avtomatik yozilmaydi, chunki qoldiqlar qo'lda kiritiladi (1-bo'lim).
   - **Terminal ko'rsatilmaydi.** Plastik endi bir necha summa yig'indisi (8.9) va terminal ekranidagi joriy summaga teng emas. Operator terminal summasini terminalning o'zidan yozadi.
   - **Server:**
     - `GET /smenalar/oxirgi` to'liq `SmenaTafsilotDto` ni faqat `Smenalar` ruxsati bor foydalanuvchiga yoki shu smenaning operatoriga beradi, boshqalarga 403.
     - Yangi `GET /smenalar/oxirgi/topshirish` endpointi `SmenaTopshirishDto(int Id, int OperatorId, string OperatorIsmi, DateTime Tugadi, long? YopishDepozit)` qaytaradi, yopilgan smena bo'lmasa 204. Ruxsati `/smenalar/joriy` bilan bir xil.
   - **Klient tartibi** (403 hech qachon chiqmasligi kerak):
     1. `Smenalar` ruxsati bo'lsa, `/smenalar/oxirgi` chaqiriladi va to'liq karta ko'rsatiladi.
     2. Aks holda `/smenalar/oxirgi/topshirish` chaqiriladi. `OperatorId` joriy foydalanuvchiga teng bo'lsa, `/smenalar/oxirgi` dan to'liq karta olinadi. Teng bo'lmasa, faqat topshirish bloki ko'rsatiladi.
11. **Eski qarz to'lovi (2026-10-11).** Dasturdan oldingi qarzdorlar pul olib keladi, lekin bu qarz dasturda yo'q. Pulni kassaga qo'shib qo'yish mumkin emas, aks holda yopishda ortiqcha chiqadi. Foydalanuvchi qarori: faqat olib kelingan summa yoziladi, "qolgan qarz" maydoni bo'lmaydi.
   - **Ekran:** "Qarz qaytdi" oynasida "Eski qarz (dasturda yo'q)" rejimi bo'ladi. Unda quyidagilar kiritiladi:
     - mijoz ismi (majburiy, mijoz takliflari bilan), telefon va mashina raqami (ixtiyoriy);
     - summa;
     - to'lov usuli: Naqd, Plastik yoki Depozit (mavjud `TolovTuri`);
     - izoh.
   - **Kim kiritadi:** `QarzQaytdi` ruxsati borlar, ya'ni operator ham, boshliq ham. Ochiq smena majburiy, chunki pul joriy smena kassasiga kiradi. Ochiq smena bo'lmasa server 409 qaytaradi, xuddi nasiya, qaytish va xarajat kabi.
   - **Server:** `POST /nasiyalar/eski-qarz` endpointi `EskiQarzYaratishDto(string MijozIsmi, string Telefon, string MashinaRaqami, long Summa, TolovTuri Usul, string? Izoh)` qabul qiladi. U bitta tranzaksiyada uch ish qiladi:
     1. Ochiq smenaga bog'langan `Nasiya` yaratadi: `EskiQarz = true`, Summa = to'lov, Muddat = bugun, darhol yopilgan.
     2. Shu summaga `NasiyaQaytishi` yozadi: `SmenaId` = ochiq smena, usul tanlangani.
     3. `NasiyaTafsilotDto` qaytaradi.
     Migratsiya bitta qo'shimcha ustun: `Nasiyalar.EskiQarz`.
   - **Hisob:** eski qarz nasiyasi `NasiyaJami` ga, "oy berilgan"ga va operatorning "bergan nasiya"siga **kirmaydi**. Uning qaytishi esa `QaytganNasiya` ga kiradi. Natijada kassa formulasi o'zgarishsiz to'g'ri chiqadi: naqd bo'lsa kutilgan naqd ortadi, plastik yoki depozit bo'lsa ular orqali muvozanatlanadi.
   - **O'chirish:** eski qarz qaytishi o'chirilsa, uning nasiyasi ham birga o'chadi. Teskarisi ham shunday. Qoidalar mavjud qaytish o'chirish bilan bir xil.
   - **Ko'rinish:**
     - `NasiyaDto` va `NasiyaQaytishiDto` oxiriga `bool EskiQarz` qo'shiladi.
     - Nasiyalar ro'yxatida, Savdo'dagi qaytishlarda va smena tafsilotida "Eski qarz" belgisi chiqadi.
     - Hisobotda boshliq uchun alohida "Eski qarzlardan tushgan" summasi: `HisobotDto` oxiriga `long EskiQarzQaytgan, int EskiQarzSoni`.
     - Audit yozuvi: "Eski qarz to'landi".
     - Eski qarz nasiyasi smenaning "Nasiya berildi" ro'yxatida chiqmaydi: server `SmenaTafsilotDto.Nasiyalar` dan chiqarib tashlaydi, klient ham himoya uchun filtrlaydi. Uning qaytishi esa qaytishlar ro'yxatida belgi bilan ko'rinadi.
     - Nasiyalar ro'yxatining Jami qatoriga va Excel Jami'ga eski qarz yozuvlari kirmaydi.
     - O'chirish nasiya tafsilotidan ham mumkin, qaytishi bilan birga. Tasdiq matni `Nasiya_EskiQarzOchirish`.
12. **Nasiyalar: operator bo'yicha filtr (2026-10-11).** Foydalanuvchiga masalan Qaxxorning jami nasiyalari kerak.
   - **Server:** `GET /nasiyalar` ga `operatorId` parametri qo'shiladi (`Nasiya.OperatorId` bo'yicha). U `holat` va `q` bilan birga ishlaydi.
   - `NasiyalarDto` oxiriga `NasiyaOperatorXulosaDto? Operator` qo'shiladi, faqat `operatorId` berilganda to'ldiriladi:
     `NasiyaOperatorXulosaDto(int OperatorId, string OperatorIsmi, int Soni, long Berilgan, long Qaytgan, long Qoldiq, int FaolSoni, long EskiQarzQabul)`.
     Eski qarz yozuvlari `Soni`, `Berilgan`, `Qaytgan` va `Qoldiq` ga kirmaydi, faqat `EskiQarzQabul` da ko'rinadi.
   - **Ekran:** Nasiyalar bo'limida "Operator: Hammasi / …" tanlovi bo'ladi. Ro'yxat nasiya yozuvlaridagi operatorlardan olinadi: `NasiyaDto.MuallifId` (= `Nasiya.OperatorId`, nasiyani yozgan odam) va `OperatorIsmi`. Operator tanlanganda umumiy KPI'lar yashirinadi, o'rniga operator KPI'lari chiqadi. Operator tanlansa, KPI'lar shu operator bo'yicha chiqadi: bergan, qaytgan, qoldiq, soni va alohida "eski qarzdan qabul qilgan". Excel eksporti filtrni hisobga oladi, sarlavhada operator ismi yoziladi.

13. **Savdo va smenani yopishda bir xil pult ko'rsatkichi (2026-10-17).**
   - **Muammo.** Savdo kartasi `AparatDto.TotalLitr` ni ko'rsatadi, ya'ni oxirgi yopilgan smenadagi qiymatni. Smenani yopish sahifasi esa "Oldingi"ni ochiq smenada narx o'zgarishida qayd etilgan oxirgi segmentdan oladi. Narx smena ichida o'zgarsa, ikki joyda ikki xil raqam chiqadi. Mijozda shunday bo'ldi: Savdo'da 109 797.58, yopishda 1 099 797.58.
   - **Qoida.** Ochiq smenada aparatning joriy pult ko'rsatkichi ko'rinadigan hamma joyda manba bitta: `SmenaHisobi.Oldingi`. Bu shu aparatning ochiq smenadagi oxirgi narx o'zgarishi segmentining `Oxiri`, segment bo'lmasa `TotalLitr`. Shu qoida quyidagi joylarga tegishli:
     - Savdo kartasi;
     - smenani yopish sahifasidagi "Oldingi";
     - narx o'zgarishi dialogidagi oldingi qiymat.
   - **Izoh.** Qiymat narx o'zgarishi segmentidan olingan bo'lsa, Savdo kartasida kichik izoh chiqadi: "Narx o'zgarishida qayd etilgan". `KorsatkichTuzatish` ruxsati bo'lsa, yonida "Tuzatish" havolasi turadi (14-band).
   - **Yangilanish.** Narx o'zgarishi, 14-banddagi tuzatish yoki smena yopilishidan keyin karta SignalR orqali o'zi yangilanadi.
   - **Server o'zgarmaydi.** Kerakli ma'lumot ochiq smena tafsilotida (`SmenaTafsilotDto.Korsatkichlar`) va `AparatDto` da bor.
14. **Ochiq smenada narx o'zgarishi ko'rsatkichini tuzatish (2026-10-17).**
   - **Muammo.** Mijozda DIZEL ECO narxi o'zgarganda 4-aparatga 109 797.58 o'rniga 1 099 797.58 yozildi, natijada 990 000 L × 15 500 chiqdi. Ochiq smenada buni tuzatishning yo'li yo'q:
     - aparatni tahrirlash 409 qaytaradi;
     - ko'rsatkichni tuzatish faqat yopilgan smenada ishlaydi;
     - smenani yopib ham bo'lmaydi, chunki yopish ko'rsatkichi 1 099 797.58 dan kichik bo'lishi mumkin emas.
     fc bazani qo'lda tuzatdi: segment o'chirildi, audit #22 yozildi.
   - **Server.** `PUT /smenalar/{id}/narx-korsatkichi`.
     - Tana: mavjud `KorsatkichTuzatishDto(int AparatId, decimal Qiymat, string Sabab)`. Javob: ochiq smenaning `SmenaTafsilotDto` si. Ruxsat: `KorsatkichTuzatish`.
     - Xatolar:
       - `id` ochiq smena bo'lmasa, 409;
       - aparat topilmasa, 404;
       - aparatda shu smenada narx o'zgarishi segmenti bo'lmasa, 409: "Bu aparatda ochiq smenada narx o'zgarishi ko'rsatkichi yo'q.";
       - sabab bo'sh bo'lsa, 400.
     - Aparatning shu smenadagi **oxirgi** narx o'zgarishi segmenti tuzatiladi. `q = Yaxlitla(Qiymat)`.
       - `q < Boshi` bo'lsa, 400, mavjud "oldingi ko'rsatkichdan kichik" matni bilan.
       - `q == Oxiri` bo'lsa, 400: "Yangi ko'rsatkich joriy ko'rsatkichdan farq qilishi kerak."
       - `q == Boshi` bo'lsa, segment **o'chiriladi**, chunki narx o'zgarguncha savdo bo'lmagan. 0 litrli segment saqlanmaydi, bu `NarxOzgarishi` qoidasi bilan bir xil.
       - Boshqa hollarda `Oxiri = q` bo'ladi, `Litr` va `Summa` qayta hisoblanadi. `Narx` eski narx bo'lib qoladi.
     - Aparatning `TotalLitr` va `BakQoldiq` qiymatlari o'zgarmaydi. Harakat yozilmaydi, chunki ochiq smenada pul hali hisoblanmagan.
     - Audit: "Ko'rsatkich tuzatildi", tur `tuzatish`. Matn yangi `AuditMatnlari.NarxKorsatkichiTuzatildi(smenaId, raqam, eski, yangi, sabab)` orqali yasaladi, masalan: `Smena #4 · 4-aparat narx o'zgarishidagi ko'rsatkich 1 099 797.58 dan 109 797.58 ga · sabab: …`.
     - Saqlangach kuzatuvchilarga `SmenaOzgardi` yuboriladi, xuddi narx o'zgarishidagi kabi.
     - Testlar:
       - yuqoridagi har bir holat;
       - ruxsatsiz so'rov 403 qaytaradi;
       - 2026-10-17 ssenariysi: 1 099 797.58 dan 109 797.58 ga tuzatilganda segment o'chadi. Shundan keyin smena yopiladi, 4-aparat to'liq yangi narxda hisoblanadi va bakdan to'g'ri litr ayriladi.
   - **Ekran** (desktop va web bir xil).
     - "Tuzatish" havolasi ikki joyda chiqadi: smenani yopish sahifasidagi "N-aparat: narx o'zgargunicha …" izohi yonida va Savdo kartasidagi izoh yonida (13-band). Faqat `KorsatkichTuzatish` ruxsati bilan.
     - Dialog sarlavhasi: "N-aparat: narx o'zgarishidagi ko'rsatkich". Ichida:
       - hozirgi qiymat, faqat o'qish uchun;
       - "To'g'ri ko'rsatkich" maydoni;
       - "Sabab" maydoni, majburiy;
       - jonli hisob: "narx o'zgargunicha X L × narx = summa".
     - Agar `q == Boshi` bo'lsa, izoh chiqadi: "Narx o'zgarguncha savdo bo'lmagan, bu qator o'chiriladi."
     - Enter tartibi 8.4 bo'yicha. Server xatosi dialog ichida ko'rinadi.
15. **Bakdagidan ko'p litr chiqsa ogohlantirish (2026-10-17).**
   - **Qayerda.** Pult ko'rsatkichi kiritiladigan hamma joyda:
     - narx o'zgarishi dialogi;
     - smenani yopish qatorlari;
     - 14-band dialogi;
     - yopilgan smena ko'rsatkichini tuzatish dialogi.
   - **Hisob.** Klientda bajariladi, server o'zgarmaydi. Kiritilayotgan qator litri = yangi ko'rsatkich − shu qatorning boshlanishi. Boshlanish har joyda boshqacha:
     - yopishda "Oldingi";
     - narx o'zgarishida joriy oldingi qiymat (13-band);
     - 14-bandda segmentning `Boshi`;
     - yopilgan smena tuzatishida segmentning boshi.
   - **Ochiq smenada solishtirish** (yopish, narx o'zgarishi, 14-band) aparat bo'yicha jami litr bilan qilinadi. 2026-10-17 dagi kelishuvga ko'ra, manfiy "bakda bor" ko'rsatilmaydi.
     - chiqyapti = shu smenada shu aparatdan chiqadigan jami litr: qayd etilgan narx o'zgarishi segmentlari + kiritilayotgan qator. Tuzatilayotgan segment yangi qiymati bilan olinadi.
     - bor = `AparatDto.BakQoldiq`, chunki smena yopilguncha bakdan hech narsa ayrilmaydi.
   - **Yopilgan smenani tuzatishda:** chiqyapti = tuzatilayotgan segmentning yangi litri, bor = `BakQoldiq` + shu segmentning eski litri. Bu eski litr yopishda bakdan ayrilgan edi.
   - Qator bo'sh bo'lsa ogohlantirilmaydi.
   - **Ogohlantirish.** chiqyapti > bor bo'lsa:
     - maydon ostida sariq matn chiqadi: "Bakda {bor} L bor, {chiqyapti} L chiqyapti. Ko'rsatkichni tekshiring.";
     - saqlash tugmasi bosilganda (smenani yopish, narx o'zgarishi yoki tuzatish) tasdiq dialogi chiqadi. Unda shunday aparatlar ro'yxati, masalan "4-aparat: 990 000 L (bakda 5 017.83 L)", va ikki tugma bo'ladi: "Tekshiraman" (fokus shunda, dialog yopiladi) va "Baribir saqlash".
     - Smenani yopishda bak tasdig'i odatdagi "Smenani yopasizmi?" dan **oldin** chiqadi. "Baribir saqlash" bosilgandan keyin "Smenani yopasizmi?" chiqadi. Ogohlantirish bo'lmasa, avvalgidek darhol "Smenani yopasizmi?" chiqadi.
     - Escape faqat eng ustki oynani yopadi.
   - **Bloklamaydi**, chunki bak qoldig'i eskirgan bo'lishi mumkin, masalan kirim hali kiritilmagan bo'lsa.
   - 13–15-band matnlari Til.cs ga qo'shiladi (uz, kiril, ru). Kalitlarni fc-desktop qo'shadi va ro'yxatini fc-web ga yuboradi. fc-web lug'atni `npm run lugat` bilan yangilaydi. Mavjud mos kalit bo'lsa, qayta ishlatiladi.
   - **Migratsiya yo'q.** Versiya 0.1.4 bo'ladi. Installer e2e 0.1.3 dan yangilanishni tekshiradi: ochiq smenada narx o'zgarishi segmenti bilan, keyin u yangi endpoint orqali tuzatiladi.
16. **Pult krugi: 10 000 da 0 ga qaytadigan pult (2026-10-17).**
   - **Muammo.** Mijozdagi 5-aparat pulti 9 999.99 gacha sanaydi. 10 000 ga yetganda 0 dan qayta boshlaydi va pultdagi "krug" hisoblagichi 1 ga oshadi. Hozir pultda krug 2, 560.52 turibdi. Dastur krugni bilmagani uchun operator 10 000 ni qo'lda qo'shib, 10 560.52 deb yozgan.
   - **Asosiy qoida.**
     - Dastur ichida va API'da ko'rsatkich doim **umumiy litr** bo'lib qoladi: `TotalLitr`, segmentlarning `Boshi`/`Oxiri`, `AparatKorsatkichDto.Qiymat`. Litr, summa, bak va hisobotlar formulasi o'zgarmaydi.
     - Krug faqat ekranda ko'rsatish va kiritish qoidasi. Pult qiymatini umumiy litrga klient o'giradi.
   - **Server va shartnoma.**
     - `Aparat.KrugHajmi` qo'shiladi: `decimal?`, L. `null` bo'lsa krug yo'q. Migratsiya bitta nullable ustun qo'shadi, mavjud qatorlar `null` bo'lib qoladi.
     - `AparatDto` oxiriga `decimal? KrugHajmi` qo'shiladi.
     - `AparatYaratishDto` oxiriga `decimal? KrugHajmi = null`: `null` yoki `0` bo'lsa krug yo'q.
     - `AparatTahrirlashDto` oxiriga `decimal? KrugHajmi = null`. Bu yerda ma'nosi boshqacha, chunki eski klient maydonni yubormasa, sozlama o'chib qolmasligi kerak:
       - `null` – **o'zgarmaydi**;
       - `0` – krug o'chiriladi;
       - `> 0` – krug hajmi o'rnatiladi.
     - Manfiy qiymat 400 qaytaradi.
     - O'zgarsa audit "Aparat o'zgartirildi" yoziladi, matnida "krug 10 000 L" yoki "krug yo'q" bo'ladi.
     - Krug hajmini istalgan payt o'zgartirish mumkin, ochiq smena ham to'sqinlik qilmaydi, chunki bu faqat ko'rinish. `AparatOzgardi` yangi qiymat bilan yuboriladi.
   - **Ko'rsatish.** Umumiy qiymat T, krug hajmi H bo'lsa:
     - `krug = floor(T / H) + 1`;
     - `pult = T − (krug − 1) × H`.
     - Misol: 10 560.52 bo'lsa, pult **560.52**, ostida kichik "krug 2". T = 10 000.00 bo'lsa, pult 0.00, krug 2.
     - Bu qoida ko'rsatkich ko'rinadigan hamma joyga tegishli: Savdo kartasi, smenani yopishdagi "Oldingi", narx o'zgarishi dialogi, smenalar tarixi va tafsiloti (Boshi/Oxiri), Sozlamalardagi aparat, tuzatish dialoglari.
   - **Kiritish.** Krugli aparatga operator pultda ko'rgan qiymatni yozadi.
     - Qiymat `0 ≤ p < H` bo'lishi shart. Aks holda maydonda xato chiqadi: "Pult ko'rsatkichi {H} dan kichik bo'lishi kerak".
     - Asos B — o'sha qatorning boshlanishi:
       - yopish va narx o'zgarishida "Oldingi";
       - 14-band va yopilgan smena tuzatishida segmentning `Boshi`.
     - `kB = floor(B / H) + 1`, `pB = B − (kB − 1) × H`.
     - `p ≥ pB` bo'lsa, umumiy = `(kB − 1) × H + p`, krug o'zgarmaydi.
     - `p < pB` bo'lsa, pult 0 dan qayta boshlangan: umumiy = `kB × H + p`, krug 1 ga oshadi. Maydon ostida "krug kB+1" yozuvi ajralib turadi.
     - Sotilgan litr = umumiy − B. Bir kiritishda faqat bitta krug aniqlanadi.
     - Misollar:
       - Oldingi 10 560.52 (560.52, krug 2), yozildi 1 250.30: umumiy 11 250.30, 689.78 L.
       - Oldingi 9 800.00 (krug 1), yozildi 350.00: umumiy 10 350.00, krug 2, 550.00 L.
   - **Tasdiq.** Saqlashda (smenani yopish, narx o'zgarishi, tuzatishlar) krug oshgan aparat bo'lsa, tasdiq dialogi chiqadi.
     - Sarlavha: "Pult krugi oshdi".
     - Qatorlar: "{N}-aparat: pult 0 dan qayta boshlangan, krug {k} → {k+1}, sotilgan {L} L".
     - Tugmalar: "Tekshiraman" (fokus shunda) va "Ha, to'g'ri".
     - Tartib: avval krug tasdig'i, keyin bak tasdig'i (15-band), keyin "Smenani yopasizmi?".
   - **Sozlamalar → aparat.**
     - "Pult 0 ga qaytadi (krug)" belgisi va "Krug hajmi, L" maydoni bo'ladi. Odatda 10 000.
     - Krug yoqilgan aparatda pultni tuzatish va boshlang'ich qiymat ikki maydon bilan kiritiladi: "Pult ko'rsatkichi" va "Krug". Umumiy qiymat `(krug − 1) × H + pult` bo'ladi.
     - Joriy holat "560.52 · krug 2" ko'rinishida chiqadi.
   - **Desktopning NamunaServer'i** ham `KrugHajmi` ni qo'llab-quvvatlaydi.
   - **Mijozda.** O'rnatilgandan keyin 5-aparatga `KrugHajmi = 10 000` yoqiladi. Mavjud 10 560.52 o'zgarmaydi, darhol "560.52 · krug 2" bo'lib ko'rinadi.
   - **Testlar.**
     - Backend: migratsiya, DTO, tahrirlashning null/0/>0 ma'nolari, 400, audit, SignalR.
     - Klientlar: o'girish va krug aniqlash testlari, yuqoridagi misollar, `p ≥ H` xatosi, tasdiq dialoglari tartibi va skrinshotlar.
     - Installer e2e 0.1.3 → 0.1.4: ustun qo'shiladi, barcha ma'lumotlar o'zgarishsiz qoladi. Keyin 5-aparatga krug yoqiladi va smena krug bilan yopiladi.

17. **Zaxira nusxa: E:\baza, 30 ta fayl, smena yopilganda va Telegram'ga (2026-11-01).**
   - **Muammo.** Zaxira `C:\ProgramData\FuelControl\zaxira` da turadi. Bu papka yashirin, shuning uchun foydalanuvchi uni topa olmadi. Hozir nusxa kuniga bir marta olinadi va 30 kundan eskilari o'chiriladi. Nusxa kompyuterdan tashqariga chiqmaydi.
   - **Papka.** `Zaxira:Papka` sozlamasi o'zgarmaydi. Mijozda fc uni `appsettings.Local.json` orqali `E:\baza` qiladi.
     - Papka mavjud bo'lmasa, yozib bo'lmasa yoki disk ulanmagan bo'lsa, nusxa standart papkaga (`zaxira`) olinadi.
     - Bu holat logga va Sozlamalar → Zaxira nusxa'dagi holatga yoziladi. Zaxira hech qachon to'xtab qolmasligi kerak.
   - **Saqlash.** Kun bo'yicha emas, son bo'yicha: eng yangi **30** ta `fuelcontrol-*.db` qoladi, eskilari o'chiriladi. Son `Zaxira:Saqlash` sozlamasidan olinadi, standarti 30.
   - **Qachon olinadi** (2026-11-01, foydalanuvchi: "har smena yopilganda bo'ldi, ortiqcha kerak emas"):
     - faqat har **smena yopilganda**: yopish javobidan keyin fon rejimida, yopishni sekinlashtirmaydi va xato bo'lsa ham yopishni buzmaydi;
     - kunlik avtomatik nusxa **olib tashlanadi**;
     - Sozlamalardagi "Hozir nusxa olish" tugmasi qoladi: u faqat qo'lda bosilganda ishlaydi.
     - 30 ta nusxa taxminan 30 ta smenaga yetadi.
   - **Telegram.** Har yangi nusxadan keyin fon rejimida Telegram'ga yuboriladi.
     - Nusxa gzip qilinadi va Bot API `sendDocument` orqali yuboriladi. Izohda shoxobcha, sana, sabab (kunlik / smena #N yopildi / qo'lda) va hajm bo'ladi.
     - Yuborilmasa (internet yo'q va h.k.), xato logga va holatga yoziladi. Nusxa keyingi safar qayta urinish bilan yuboriladi: oxirgi yuborilmagan nusxa ham ketadi.
     - Bot API cheklovi 50 MB. Undan katta bo'lsa, yuborilmaydi va holatda ko'rinadi.
     - Sozlamalar: bot tokeni va chat ID. Token serverda saqlanadi va klientga hech qachon to'liq qaytmaydi (faqat `…ab12` ko'rinishida). Token audit matniga ham, logga ham yozilmaydi. Qayerda saqlash (baza jadvali yoki ProgramData'dagi fayl) — backend taklif qiladi, migratsiya kerak bo'lsa shartnomani fc bilan kelishadi.
     - Yoqilmagan (token yo'q) bo'lsa, hech narsa yuborilmaydi.
   - **Sozlamalar → Zaxira nusxa** (desktop va web bir xil, `Sozlamalar` ruxsati):
     - papka yo'li va standartga qaytgan bo'lsa ogohlantirish;
     - oxirgi nusxalar ro'yxati (nomi, vaqti, hajmi, sababi);
     - Telegram: token (maskalangan, yangisini kiritish mumkin), chat ID, "Yoqilgan" belgisi, **"Sinov yuborish"** tugmasi (kichik matnli xabar), oxirgi yuborish holati (vaqt, muvaffaqiyat/xato matni).
     - Token o'zgarishi auditga yoziladi, tokenning o'zi yozilmaydi.
   - **Testlar.**
     - 31-nusxada eng eskisi o'chadi;
     - papka mavjud bo'lmasa, nusxa standart papkaga tushadi;
     - smena yopilgach nusxa olinadi;
     - Telegram: soxta HTTP handler bilan muvaffaqiyat, xato, qayta urinish va 50 MB;
     - token javobda va logda yo'q;
     - ruxsatsiz 403.
   - **Versiya 0.1.5.** Mijozda o'rnatilgach, fc `Zaxira:Papka = E:\baza` qiladi. Bot tokenini foydalanuvchi o'zi Sozlamalarga kiritadi (BotFather'da bot ochib, chat ID ni oladi).

18. **Ishlab turgan foydalanuvchi tizimdan chiqarib yuborilmasin (2026-11-04).**
   - **Muammo.** Token 12 soat amal qiladi (`Avtorizatsiya.Muddat`), smena esa 24 soat. 12 soatdan keyin birinchi so'rov 401 qaytaradi va klient "Sessiya tugadi" deb kirish oynasiga qaytaradi. Shunda ochiq dialogga yozilgan narsalar yo'qoladi. Mijozda Jabborali "Eski qarz"ni bosganda shunday bo'lgan, foydalanuvchiga bu "dasturdan chiqib ketish" bo'lib ko'rindi. Windows jurnalida qulash yo'q.
   - **Server.**
     - `POST /auth/yangila` (token bilan) yangi `LoginJavobiDto` qaytaradi: yangi token va yangi muddat, xuddi login kabi.
     - Foydalanuvchi nofaol yoki bloklangan bo'lsa 401 qaytadi. Ruxsat va rol bazadan olinadi, xuddi `/me` kabi.
     - Token muddati 12 soat bo'lib qoladi. Audit yozilmaydi.
   - **Klientlar (desktop va web bir xil).**
     - **Uzaytirish.** Token muddatining yarmidan ko'pi o'tgan bo'lsa (6 soatdan kam qolgan) va foydalanuvchi faol bo'lsa (oxirgi 30 daqiqada API so'rovi yoki klaviatura/sichqoncha harakati bo'lgan), fon rejimida `/auth/yangila` chaqiriladi va token almashtiriladi. Bu ko'zga ko'rinmaydi. Bir vaqtda faqat bitta yangilash so'rovi ketadi.
     - **Tekshiruv.** Taymer har 10 daqiqada tekshiradi. Desktopda DispatcherTimer emas, `Task.Delay` ishlatiladi. Har so'rovdan oldin ham tekshiriladi.
     - **SignalR.** Token almashganda u yangi tokenni oladi: AccessTokenProvider joriy tokenni qaytaradi.
     - **Muddat baribir tugasa** (kompyuter uxlagan, 12 soatdan ko'p harakatsiz qolgan): 401 bo'lganda foydalanuvchi kirish oynasiga **otilmaydi**. Joriy ekran va ochiq dialog ustida "Sessiya tugadi — parol/PIN ni qayta kiriting" oynasi chiqadi. Login o'zgartirilmaydi, faqat parol yoki PIN so'raladi. Muvaffaqiyatli kirilgach, rad etilgan so'rov bir marta qayta yuboriladi va dialogdagi ma'lumot saqlanib qoladi.
     - **Boshqa odam kirmoqchi bo'lsa,** u "Chiqish" ni bosadi va to'liq kirish oynasi ochiladi.
     - **Nofaol yoki bloklangan foydalanuvchi.** `/auth/yangila` yoki qayta kirish 401/403 bersa, avvalgidek to'liq chiqiladi.
   - **Testlar.**
     - Server: yangila — yangi token, nofaol foydalanuvchi uchun 401, eski token muddat tugaguncha ishlaydi.
     - Klientlar: muddat soxtalashtiriladi (masalan 2 daqiqalik token yoki soat siljitiladi). Faol foydalanuvchi uchun token o'zi yangilanadi. Muddat tugagach "Eski qarz" dialogiga yozilgan ma'lumot qayta kirishdan keyin joyida qoladi va so'rov o'tadi. SignalR yangi token bilan ulanadi.
   - **Versiya 0.1.6.** Ichida desktopning global xato ushlagichi ham bor (`Services/XatoJurnali.cs`, log `%LOCALAPPDATA%\FuelControl\logs`). Migratsiya yo'q.

## 9. Ish tartibi

- Faqat o'z papkangizga tegasiz: backend `src/backend` + `tests`, web `src/frontend/web`, desktop `src/frontend/desktop`. `docs/` — faqat fc.
- Shartnomani o'zgartirish kerak bo'lsa — avval fc'ga yozing, o'zingiz o'zgartirmang.
- Portlar: backend API :5000 (Development, dev.db); web o'z API'si :5100 (env Web); test API :5199. Build va test — `--artifacts-path <scratchpad>` (ishlayotgan API bin'ni qulflaydi). Boshqa sessiya jarayonini to'xtatmang.
- **Commit qilmang** — fc ko'rib chiqib, foydalanuvchi bilan kelishib commit qiladi.
- Har bosqich oxirida fc'ga qisqa hisobot: nima qilindi, qanday tekshirildi (buyruq va natija), ochiq savollar. Noaniq joyda taxmin qilmay fc'dan so'rang.
