# FuelControl — mijozga o'rnatish yo'riqnomasi

Bitta fayl: `FuelControl-Setup-<versiya>.exe`. U serverni (API + web), desktop dasturni va ixtiyoriy Cloudflare Tunnel'ni
o'rnatadi. .NET alohida o'rnatilmaydi (hammasi o'rnatuvchi ichida). Bu hujjat: o'rnatish, birinchi kirish, yangilash, zaxira,
port, Cloudflare — va oxirida dasturchilar uchun sinov yo'riqnomasi hamda o'rnatuvchini yig'ish.

## 1. Nima o'rnatiladi

| Komponent | Nima | Qayerga |
|---|---|---|
| **Server** | API + web (PWA). Windows xizmati **FuelControl** (avtomatik ishga tushadi, xatoda o'zi qayta ishga tushadi) | `C:\Program Files\FuelControl\Server\` |
| **Desktop** | Kassir/boshliq dasturi. Start menyu va ish stolida yorliq | `C:\Program Files\FuelControl\Desktop\` |
| **Cloudflare Tunnel** (ixtiyoriy) | Serverni internetdan HTTPS orqali ochadi. Xizmat **Cloudflared** | `C:\Program Files\FuelControl\Tunnel\` |

Ma'lumotlar dastur papkasida **emas**, `%ProgramData%\FuelControl\` da (odatda `C:\ProgramData\FuelControl\`):

| Nima | Qayerda |
|---|---|
| Baza | `fuelcontrol.db` |
| Zaxira nusxalar | `zaxira\fuelcontrol-YYYYMMDD-HHmmss-smena<N>.db` (papkani `Zaxira:Papka` o'zgartiradi, 7 va 9-bo'lim) |
| Jurnallar | `logs\fuelcontrol-YYYYMMDD.log` |

Bu papkaga faqat **Administrators** va **SYSTEM** kira oladi. Dasturni o'chirganda ham, yangilaganda ham u **o'chirilmaydi**.

O'rnatish turlari:

- **To'liq (server + desktop)** — shoxobchadagi asosiy kompyuter.
- **Faqat desktop (boshqa kompyuter)** — qo'shimcha kassa/boshliq kompyuterlari; ular asosiy kompyuterdagi serverga ulanadi.
- **Maxsus** — komponentlarni o'zingiz tanlaysiz (Cloudflare Tunnel faqat shu yerda yoki "To'liq" da qo'lda belgilanadi).

## 2. Talablar

- Windows 10 (1607 va undan yangi), Windows 11 yoki Windows Server 2016+, 64-bit.
- **Server** komponenti uchun administrator huquqi (Windows xizmati va xavfsizlik devori qoidasi). "Faqat desktop" uchun shart emas, lekin o'rnatuvchi baribir administrator so'raydi.
- Server kompyuterida tanlangan port (standart **5000**) boshqa dastur tomonidan band bo'lmasligi kerak.
- Internet shart emas (Cloudflare Tunnel va Pages'dan tashqari).

## 3. To'liq o'rnatish (server + desktop)

1. `FuelControl-Setup-<versiya>.exe` ni ikki marta bosing. Windows so'rasa **Ha** (administrator).
2. **O'rnatish turi**: "To'liq (server + desktop)". Cloudflare Tunnel kerak bo'lsa, komponentlar ro'yxatida "Cloudflare Tunnel" ni ham belgilang.
3. **Server sozlamalari** sahifasi:
   - *Server porti* — standart 5000. Band bo'lsa o'rnatuvchi ogohlantiradi.
   - *Administrator paroli* (kamida 8 belgi) va uni tasdiqlash. Bu `admin` foydalanuvchining birinchi kirish paroli. Bu maydonlar **faqat baza hali yo'q bo'lganda** so'raladi.
4. *(Tunnel belgilangan bo'lsa)* **Cloudflare Tunnel** sahifasi: tokenni qo'ying (11-bo'lim). Bo'sh qoldirsangiz, Tunnel o'rnatilmaydi.
5. **O'rnatish** tugmasi. O'rnatuvchi fayllarni yozadi, sozlamalarni yaratadi, xizmatni ro'yxatdan o'tkazadi, xavfsizlik devorida portni ochadi va xizmatni ishga tushirib, javob berishini tekshiradi.
6. Oxirgi sahifada web manzili ko'rsatiladi: `http://localhost:<port>` (tarmoqda: `http://<KOMPYUTER-NOMI>:<port>`).
7. Agar biror qadam bajarilmasa, o'rnatish oxirida ogohlantirish chiqadi (masalan, "xizmat ishga tushmadi" + jurnal papkasi) — 14-bo'limga qarang.

## 4. Faqat desktop (boshqa kompyuter)

1. O'rnatuvchini ishga tushiring → tur: **Faqat desktop (boshqa kompyuter)**.
2. **Server manzili** sahifasi: server o'rnatilgan kompyuterning manzili, masalan `http://192.168.1.10:5000`.
   - Server kompyuterida `ipconfig` → "IPv4 Address", yoki kompyuter nomi: `http://KOMPYUTER-NOMI:5000`.
   - O'rnatuvchi manzilga ulanib ko'radi; javob bo'lmasa "baribir davom etasizmi?" deb so'raydi (server hali o'rnatilmagan bo'lishi mumkin).
3. Tugagach desktop'ni oching: kirish ekranida pastda "Server: http://..." ko'rinadi. Keyin o'zgartirish kerak bo'lsa, shu yozuvni bosing.

Manzil `Desktop\sozlama.json` ga yoziladi. Foydalanuvchi birinchi marta kirgach, manzil uning o'z sozlamasiga (`%AppData%\FuelControl\sozlamalar.json`) saqlanadi va keyin o'sha ustun turadi: server porti keyinroq o'zgarsa, desktop kirish ekranidagi server manzilini yangilang.

## 5. Birinchi kirish

- Login: **`admin`**, parol — o'rnatishda kiritgan parolingiz.
- Kirgach **Sozlamalar** bo'limida: yoqilg'i narxlarini, aparatlarni (totalizator boshlang'ich qiymati bilan) tekshiring; **Foydalanuvchilar**da operatorlar va boshliqni yarating, ruxsatlarni bering.
- Boshlang'ich ma'lumot: yoqilg'i turlari AI-92 / AI-95 / Dizel va 5 ta aparat (narxlar va aparatlarni o'zingizga moslang).
- Administrator parolini keyin ham Sozlamalar → Foydalanuvchilar'dan almashtirsa bo'ladi.
- Xavfsizlik: parol sozlama faylida **saqlanib qolmaydi**. O'rnatuvchi uni vaqtincha `appsettings.Production.json` ga yozadi, server birinchi marta ishga tushib admin foydalanuvchini yaratgach, fayldan o'chiradi. Bu fayl faqat SYSTEM va Administrators uchun ochiq.

## 6. Fayllar va sozlamalar

`C:\Program Files\FuelControl\Server\` ichida:

| Fayl | Kim boshqaradi | Mazmuni |
|---|---|---|
| `appsettings.Production.json` | **O'rnatuvchi** (har o'rnatishda qayta yoziladi, qo'lda tegmang) | `Urls` (port), baza yo'li, zaxira va jurnal papkalari, JWT kaliti. Faqat SYSTEM/Administrators o'qiy oladi |
| `appsettings.Local.json` | **Administrator** (o'rnatuvchi tegmaydi) | Qo'shimcha sozlamalar, masalan `Cors:Manbalar` (7-bo'lim) |

JWT kaliti — kirish tokenlarini imzolovchi maxfiy kalit. U birinchi o'rnatishda tasodifiy yaratiladi (256 bit) va **yangilashda saqlanadi** (foydalanuvchilar tizimdan chiqib ketmaydi).

Desktop: `Desktop\sozlama.json` — o'rnatuvchi yozgan standart server manzili.

## 7. Sozlamalarni o'zgartirish (`appsettings.Local.json`)

Faylni Administrator huquqi bilan Notepad'da oching, o'zgartiring, saqlang va xizmatni qayta ishga tushiring (Administrator PowerShell):

```powershell
Restart-Service FuelControl
```

Misol — web alohida domenda (Cloudflare Pages) turganda uning manzilini API'ga ruxsat etish (CORS):

```json
{
  "Cors": { "Manbalar": [ "https://fuelcontrol.pages.dev", "https://web.misol.uz" ] }
}
```

`Manbalar` bo'sh bo'lsa CORS umuman yoqilmaydi. Manzillar aniq bo'lishi kerak (`*` emas), oxirida `/` bo'lmasin (bo'lsa ham olib tashlanadi).

Misol — zaxira nusxalarni topish oson papkaga (masalan, `E:\baza`) olish va Telegram izohida shoxobcha nomini ko'rsatish (9-bo'lim):

```json
{
  "Zaxira": { "Papka": "E:\\baza", "Saqlash": 30, "Shoxobcha": "Chilonzor shoxobchasi" }
}
```

JSON'da teskari chiziq ikkita yoziladi (`E:\\baza`). Zaxira kalitlari:

| Kalit | Ma'nosi | Standarti |
|---|---|---|
| `Zaxira:Papka` | Nusxalar tushadigan papka. Yo'q bo'lsa (disk ulangan bo'lsa) yaratiladi | `%ProgramData%\FuelControl\zaxira` (o'rnatuvchi yozadi) |
| `Zaxira:Saqlash` | Nechta eng yangi nusxa saqlanadi; eskilari o'chiriladi | `30` |
| `Zaxira:StandartPapka` | `Papka` ga yozib bo'lmasa nusxa tushadigan papka | bazaning yonidagi `zaxira` (`%ProgramData%\FuelControl\zaxira`) |
| `Zaxira:Shoxobcha` | Telegram izohidagi shoxobcha nomi | kompyuter nomi |

## 8. Yangilash

Yangi `FuelControl-Setup-<versiya>.exe` ni o'sha kompyuterda shunchaki ishga tushiring (avval eskisini o'chirish shart emas):

- Server xizmati to'xtatiladi → fayllar yangilanadi → xizmat qayta ishga tushadi. Baza migratsiyalari server ishga tushganda o'zi bajariladi.
- **Saqlanadi**: baza, zaxiralar, jurnallar, JWT kaliti (sessiyalar), `appsettings.Local.json`.
- Port sahifasida oldingi port ko'rinadi; administrator paroli qayta so'ralmaydi (baza bor).
- Desktop ochiq bo'lsa, o'rnatuvchi uni yopishni so'raydi.
- Yangilashdan oldin zaxira nusxa oling: Sozlamalar → Zaxira nusxa → "Hozir nusxa olish".
- `appsettings.Local.json` o'chirib qayta o'rnatishda (avval o'chirish, keyin o'rnatish) o'chadi: undagi CORS va Zaxira sozlamalarini oldin saqlab qo'ying. Oddiy yangilashda (ustidan o'rnatish) u saqlanadi.

## 9. Zaxira (backup)

- **Qachon olinadi.** Har **smena yopilganda** (yopish javobidan keyin, fonda — yopishni sekinlashtirmaydi va xato bo'lsa ham buzmaydi) va qo'lda: Sozlamalar → Zaxira nusxa → "Hozir nusxa olish". Kunlik avtomatik nusxa yo'q. Fayl nomi: `fuelcontrol-YYYYMMDD-HHmmss-smena<N>.db` (qo'lda — `-qolda`; vaqt UTC). Eski versiyalardagi `fuelcontrol-YYYYMMDD-HHmmss.db` fayllar ham saqlanadi va ro'yxatda ko'rinadi.
- **Qayerda.** Standart: `%ProgramData%\FuelControl\zaxira` (yashirin papka). Topish oson bo'lishi uchun `appsettings.Local.json` da `Zaxira:Papka` ni o'zgartiring, masalan `E:\\baza` (7-bo'lim), va xizmatni qayta ishga tushiring. Papka yo'q bo'lsa va disk ulangan bo'lsa yaratiladi. Yozib bo'lmasa (disk ulanmagan, papka yaratilmaydi va h.k.) nusxa **standart papkaga** tushadi: bu Sozlamalar → Zaxira nusxa'da ogohlantirish bilan ko'rinadi va logga yoziladi — zaxira to'xtamaydi. Disk qaytgach nusxalar yana sozlangan papkaga tushadi.
- **Nechta saqlanadi.** Eng yangi 30 ta (`Zaxira:Saqlash`), eskilari o'chiriladi (ishlatilgan va standart papkada). Smena kuniga bir nechta yopilsa, 30 ta bir necha kunga yetadi — kerak bo'lsa sonni oshiring.
- **Telegram'ga yuborish (ixtiyoriy).** Har yangi nusxa gzip qilinib botingiz orqali Telegram'ga yuboriladi (izohda shoxobcha, sana, sabab va hajm). Sozlash: (1) Telegram'da `@BotFather` → `/newbot` → bot tokenini oling; (2) botga `/start` yozing (guruhga yuborish uchun botni guruhga qo'shing) va `https://api.telegram.org/bot<TOKEN>/getUpdates` ni brauzerda oching — `chat` → `id` sizning chat ID'ingiz (guruhda manfiy son); (3) Sozlamalar → Zaxira nusxa: token va chat ID ni kiriting, "Yoqilgan" belgisini qo'ying, "Sinov yuborish" tugmasi bilan tekshiring. Bilish kerak: token serverda shifrlangan saqlanadi (shu kompyuterdan boshqasida ochilmaydi), ekranda faqat oxirgi 4 belgisi (`…ab12`) ko'rinadi, logga va auditga yozilmaydi; yuborilmasa (internet yo'q) xato Sozlamalarda ko'rinadi va keyingi nusxada (hamda har soatda) yuborilmagan oxirgi nusxa qayta yuboriladi; yoqilgan paytdan oldingi nusxalar yuborilmaydi; Telegram cheklovi 50 MB (gzip'dan keyin) — kattasi yuborilmaydi va holatda ko'rinadi.
- Zaxiralar **shu kompyuterning o'zida** (yoki shu kompyuterga ulangan diskda) — kompyuter buzilsa, ular ham yo'qoladi. Telegram'ga yuborish yoki `zaxira\` papkasini muntazam tashqi diskka/boshqa kompyuterga nusxalash shundan himoya qiladi.
- Tiklash (Administrator PowerShell):

```powershell
Stop-Service FuelControl
$d = "$env:ProgramData\FuelControl"
Rename-Item "$d\fuelcontrol.db" "fuelcontrol.db.eski" -ErrorAction SilentlyContinue
Remove-Item "$d\fuelcontrol.db-wal", "$d\fuelcontrol.db-shm" -ErrorAction SilentlyContinue
Copy-Item "$d\zaxira\fuelcontrol-YYYYMMDD-HHmmss.db" "$d\fuelcontrol.db"
Start-Service FuelControl
```

## 10. Port va xavfsizlik devori

- Standart port — **5000**. O'rnatishda o'zgartiriladi (yangilashda ham: o'rnatuvchini qayta ishga tushirib yangi port kiriting).
- O'rnatuvchi Windows xavfsizlik devorida **"FuelControl API"** nomli kiruvchi (TCP) qoida yaratadi; port o'zgarsa eski qoidani o'chirib yangisini qo'shadi. Qo'lda tekshirish: `netsh advfirewall firewall show rule name="FuelControl API"`.
- API oddiy **HTTP** (shoxobcha tarmog'i ichida). Internetga faqat Cloudflare Tunnel (HTTPS) orqali chiqaring — routerda portni to'g'ridan-to'g'ri ochmang.
- Port o'zgargach, boshqa kompyuterlardagi desktop'larda server manzilini yangilash kerak (4-bo'lim).

## 11. Cloudflare Tunnel (ixtiyoriy)

Maqsad: serverni internetdan HTTPS orqali ko'rinadigan qilish (masalan, web Cloudflare Pages'da turganda yoki telefondan kirish uchun).

**Token olish:**

1. Cloudflare dashboard → **Zero Trust** → **Networks** → **Tunnels** → **Create a tunnel** → **Cloudflared** → nom bering (masalan `shoxobcha`).
2. "Install and run connectors" qadamida **Windows** ni tanlang. Ko'rsatilgan buyruqdagi uzun matn — **token**: `cloudflared.exe service install <TOKEN>`. Tokenni (yoki butun buyruqni) nusxalang. Tokenni hech kimga bermang.
3. O'rnatuvchining Cloudflare sahifasiga qo'ying. O'rnatuvchi `cloudflared.exe` ni o'rnatadi va `cloudflared service install <token>` ni bajaradi (xizmat **Cloudflared**, avtomatik ishga tushadi).

**Manzil ulash** (o'rnatuvchi buni qilmaydi):

4. Cloudflare'da tunnel → **Public Hostname** → **Add**: domen/subdomen (masalan `api.misol.uz`), **Service**: `HTTP`, **URL**: `localhost:5000` (o'rnatishda tanlagan port).
5. Tekshirish: brauzerda `https://api.misol.uz/openapi/v1.json` ochilishi kerak.

Qo'shimcha:

- Token faqat o'rnatuvchining Cloudflare sahifasida so'raladi; bo'sh qoldirsangiz Tunnel o'rnatilmaydi. Tokenni almashtirish: o'rnatuvchini qayta ishga tushirib yangi token kiriting.
- Mashinada boshqa joydan o'rnatilgan `Cloudflared` xizmati bo'lsa, o'rnatuvchi unga tegmaydi (ogohlantirish chiqaradi).
- Web boshqa domenda bo'lsa, `Cors:Manbalar` kerak (7-bo'lim) — pastdagi Pages bo'limiga qarang.

## 12. Web'ni Cloudflare Pages'ga joylash

Web (PWA) alohida joyda — Cloudflare Pages'da, API esa shoxobchada (Cloudflare Tunnel orqali, **boshqa domenda**, masalan `https://api.misol.uz`) ishlaydi. Ilova API manzilini build vaqtida emas, **ish vaqtida** saytning `sozlama.json` faylidan oladi, shuning uchun manzil o'zgarsa qayta yozib chiqish shart emas.

Oldindan kerak:

- API tashqaridan **HTTPS** orqali ochiladi (Tunnel, 11-bo'lim). Brauzer HTTPS sahifadan oddiy `http://` API'ga so'rov yubormaydi (mixed content).
- API'da `Cors:Manbalar` ga Pages manzili qo'shilgan (12.4-band).

Agar web alohida joylanmasa, bu bo'lim kerak emas: server web'ni o'zi beradi (`http://<server>:<port>`).

### 12.1. Repo'ni ulash

1. Cloudflare dashboard → **Workers & Pages** → **Create** → **Pages** → **Connect to Git**.
2. GitHub'da `avazbekm/FuelControl` (private) repo'sini tanlang; so'ralsa Cloudflare GitHub ilovasiga shu repo uchun ruxsat bering.
3. Production branch: `main`. Har `git push` dan keyin Pages o'zi qayta quradi va joylaydi.

### 12.2. Build sozlamalari

| Maydon | Qiymat |
|---|---|
| Framework preset | None |
| Root directory | `src/frontend/web/FuelControl.Web` |
| Build command | `npm ci && npm run build:pages` |
| Build output directory | `dist/pages` |

Environment variables (Settings → Variables and Secrets):

| O'zgaruvchi | Qiymat | Izoh |
|---|---|---|
| `API_MANZIL` | `https://api.misol.uz` | API manzili — build oxirida `dist/pages/sozlama.json` ga yoziladi. **Berilmasa Pages'da ilova ishlamaydi** (ogohlantirish chiqadi). |
| `NODE_VERSION` | `24` | Angular 22 Node `^22.22.3`, `^24.15` yoki yangisini talab qiladi. Repo'da `.node-version` (`24`) ham bor — Pages uni o'qiydi. |

### 12.3. `sozlama.json` — API manzili

Fayl sayt ildizida: `{ "api": "" }`.

- `""` (bo'sh) — API shu domenda (hozirgi holat: API ilovani o'zining `wwwroot`'idan beradi).
- `"https://api.misol.uz"` — to'liq manzil, oxirida `/` bo'lmasin. Login, barcha so'rovlar va SignalR (`/hub`) shu manzilga ketadi.
- Fayl yo'q yoki buzuq bo'lsa, yoki manzil yaroqsiz (`http`/`https` emas) bo'lsa — bo'sh deb olinadi.
- Internet uzilib fayl o'qilmasa — oxirgi muvaffaqiyatli qiymat ishlatiladi (offline navbat to'g'ri domenga yuborilishi uchun).

Manzilni o'zgartirish: Pages'da `API_MANZIL` ni yangilab qayta deploy qiling (yoki `src/frontend/web/FuelControl.Web/public/sozlama.json` ni tahrirlab `git push`). `sozlama.json` doim yangi olinadi: serverda `Cache-Control: no-cache` (`pages/_headers`), service worker'da network-first.

### 12.4. API'da CORS

Ilova API'ga boshqa domendan murojaat qiladi, shuning uchun **server kompyuterida** `C:\Program Files\FuelControl\Server\appsettings.Local.json` dagi `Cors:Manbalar` ga Pages manzillari qo'shilishi shart (aniq manbalar, `*` emas; fayl va xizmatni qayta ishga tushirish — 7-bo'lim):

```json
{
  "Cors": {
    "Manbalar": [
      "https://fuelcontrol.pages.dev",
      "https://web.misol.uz"
    ]
  }
}
```

`web.misol.uz` — maxsus domen bo'lsa. Brauzer yuboradigan sarlavhalar: `Authorization`, `Content-Type`, `X-SignalR-User-Agent`; metodlar: `GET, POST, PUT, DELETE, OPTIONS`. API ularning hammasiga ruxsat beradi (preflight javobi 1 soat keshlanadi). Cookie ishlatilmaydi (JWT sarlavhada); API `AllowCredentials` ni yoqib qo'ygan, bu aniq manbalar bilan zararsiz.

### 12.5. Tekshirish

1. Pages manzilini oching → kirish sahifasi chiqadi.
2. DevTools → Network: `sozlama.json` Pages domenidan, `auth/login` va `hub` esa API domenidan ketayotganini ko'ring; Console'da `blocked by CORS policy` bo'lmasin.
3. Kiring: boshqaruv paneli ma'lumot ko'rsatsin, tepada "Aloqa yo'q" banneri chiqmasin (SignalR ulangan).

### 12.6. PWA o'rnatish (HTTPS)

Pages sayti HTTPS beradi, shuning uchun o'rnatish ishlaydi:

- **Android (Chrome):** kirish sahifasida yoki Sozlamalarda "Ilovani o'rnatish" tugmasi chiqadi.
- **iPhone (Safari):** Ulashish → **Bosh ekranga qo'shish** (ilova yo'riqnomani o'zi ko'rsatadi).
- Yangi versiya deploy qilinsa, ilova ochiq turganda "Yangi versiya tayyor" xabari chiqadi; "Yangilash" bosiladi.

### 12.7. Lokal sinash (Pages'ga chiqarmasdan)

```bash
cd src/frontend/web/FuelControl.Web
API_MANZIL=http://localhost:5000 npm run build:pages     # dist/pages
npx http-server dist/pages -p 4400                        # alohida port = alohida domen
```

API'ning dev konfiguratsiyasida (`appsettings.Development.json`) `Cors:Manbalar` ga `http://localhost:4400` qo'shilgan. Standart `npm run build` esa avvalgidek natijani API'ning `wwwroot`'iga yozadi (bir domenli joylash).

### 12.8. Nosozliklar

| Belgi | Sabab va yechim |
|---|---|
| Console: `blocked by CORS policy` | API'da `Cors:Manbalar` ga Pages manzili yo'q (7- va 12.4-bo'limlar; xizmatni qayta ishga tushirdingizmi?) yoki manzil aniq mos emas (`https://`, `www`, port). |
| Login'da "Aloqa yo'q" | `sozlama.json` da `api` bo'sh (Pages domenida API yo'q) yoki API'ga tashqaridan yetib bo'lmayapti. Network'da `auth/login` qayerga ketayotganini tekshiring. |
| Mixed content xatosi | API `http://` — Tunnel orqali `https://` bering. |
| Eski API manzili qoldi | `sozlama.json` ni brauzer/kesh ushlab qolgan bo'lishi mumkin emas (`no-cache`); Pages'da `API_MANZIL` qiymati to'g'ri ekanini va qayta deploy qilinganini tekshiring. |
| Pages build yiqildi: Node versiyasi | `NODE_VERSION=24` bering (12.2-jadval). |

## 13. Jim o'rnatish (IT uchun)

Standart Inno Setup parametrlari ishlaydi; qo'shimcha parametrlar:

| Parametr | Ma'nosi |
|---|---|
| `/PORT=5000` | Server porti |
| `/ADMINPAROL=...` | Administrator paroli (faqat baza hali yo'q bo'lsa kerak; kamida 8 belgi). Buyruq satrida ko'rinadi — maxfiy muhitda ehtiyot bo'ling |
| `/SERVERURL=http://server:5000` | "Faqat desktop" uchun server manzili |
| `/TUNNELTOKEN=...` | Cloudflare tunnel tokeni (bo'sh bo'lsa Tunnel o'rnatilmaydi) |
| `/DATADIR=D:\FuelControlData` | Ma'lumotlar papkasi (standart `%ProgramData%\FuelControl`) |
| `/COMPONENTS=server,desktop[,tunnel]` | Komponentlar |
| `/DIR="D:\FuelControl"` | O'rnatish papkasi |
| `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` | Jim rejim |
| `/LOG="C:\Temp\fuelcontrol-setup.log"` | O'rnatuvchi jurnali. Diqqat: Inno buyruq satrini jurnalning boshiga (`Setup command line:`) o'zi yozadi, shuning uchun `/ADMINPAROL` va `/TUNNELTOKEN` buyruq satrida berilgan bo'lsa, ular jurnalda ham ko'rinadi — bunday jurnalni boshqalarga yubormang. O'rnatuvchining o'z kodi parol va tokenni jurnalga yozmaydi (xizmat buyrug'idagi token yashiriladi) |

Misol:

```powershell
.\FuelControl-Setup-0.0.1.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /COMPONENTS=server,desktop /PORT=5000 /ADMINPAROL=KuchliParol123
```

Noto'g'ri qiymat (masalan, qisqa parol yoki band port) bo'lsa o'rnatuvchi hech narsa o'rnatmay chiqadi.

## 14. Muammolarni hal qilish

| Belgi | Nima qilish |
|---|---|
| O'rnatish oxirida "xizmat ishga tushmadi" | `%ProgramData%\FuelControl\logs\fuelcontrol-<sana>.log` ni oching: oxirgi `[FTL]`/`[ERR]` qatori sababni aytadi (band port, baza fayliga ruxsat, ...). Keyin `Start-Service FuelControl`. |
| Xizmat holati | `Get-Service FuelControl` (yoki `services.msc`). Windows jurnali: Event Viewer → Windows Logs → Application → manba `FuelControl`. |
| Brauzer/desktop ulanmayapti | Serverda: `curl http://localhost:5000/openapi/v1.json` (200 bo'lishi kerak). Boshqa kompyuterdan ishlamasa — xavfsizlik devori qoidasi va port (10-bo'lim). |
| Port band | O'rnatuvchi o'zi aytadi. Kim band qilganini bilish: `netstat -ano \| findstr :5000`. |
| Desktop "Aloqa yo'q" | Kirish ekranidagi server manzili to'g'rimi (4-bo'lim)? Server ishlayaptimi? |
| Admin paroli esdan chiqdi | Boshqa admin yoki boshliq (Sozlamalar ruxsati bilan) Foydalanuvchilar'dan almashtiradi. Hech kim yo'q bo'lsa — texnik yordamga murojaat qiling. |
| O'rnatuvchi nima qilganini ko'rish | `/LOG="C:\Temp\setup.log"` bilan ishga tushiring (parol/token buyruq satrida bo'lsa, jurnalni sir saqlang — 13-bo'lim). |

## 15. O'chirish

Sozlamalar → Ilovalar → **FuelControl** → O'chirish (yoki `unins000.exe`). O'chirish:

- Windows xizmatini (`FuelControl`; o'rnatilgan bo'lsa `Cloudflared`) va xavfsizlik devori qoidasini olib tashlaydi;
- dastur fayllarini va o'rnatuvchi yozgan sozlamalarni (`appsettings.*.json`, `sozlama.json`) o'chiradi;
- **baza, zaxiralar va jurnallarga (`%ProgramData%\FuelControl\`) tegmaydi.** Ular kerak bo'lmasa, papkani qo'lda o'chiring.

`appsettings.Local.json` ham o'chadi: undagi CORS va Zaxira sozlamalarini oldin saqlab qo'ying, qayta o'rnatgach tiklang.

Qayta o'rnatganda eski baza topiladi va ishlatiladi (administrator paroli qayta so'ralmaydi).

---

## 16. Sinov yo'riqnomasi (dasturchilar uchun): xizmat o'rnatish

Avtomatik tekshirilgan: o'rnatuvchining kompilyatsiyasi, fayl/sozlama yaratish, kalitni saqlash va parolni olib tashlash, API'ning
Production rejimda ishga tushishi (administratorsiz, `/NOSYSTEM=1` bilan). **Qo'lda sinash kerak** (administrator talab qiladi): Windows
xizmati, xavfsizlik devori, ruxsatlar, Cloudflare. Administrator PowerShell'da:

> Dev API (5000-port) ishlab tursa, sinov o'rnatishda **boshqa port** (masalan **5050**) tanlang.

**A. To'liq o'rnatish.** `FuelControl-Setup-<versiya>.exe` → "To'liq" → port 5050, admin paroli. Keyin:

```powershell
Get-Service FuelControl                                   # Status: Running, StartType: Automatic
sc.exe qfailure FuelControl                               # RESTART/5000, RESTART/10000, RESTART/60000
curl http://localhost:5050/openapi/v1.json                # 200
netsh advfirewall firewall show rule name="FuelControl API"   # LocalPort 5050
Get-ChildItem "$env:ProgramData\FuelControl" -Recurse     # fuelcontrol.db, zaxira\, logs\fuelcontrol-<sana>.log
icacls "$env:ProgramData\FuelControl"                     # faqat SYSTEM va Administrators
Select-String -Path "C:\Program Files\FuelControl\Server\appsettings.Production.json" -Pattern "AdminParol"   # BO'SH bo'lishi kerak
```

Desktop'ni oching (yorliq) → kirish: `admin` + kiritgan parol → boshqaruv paneli ochiladi. Brauzerda `http://localhost:5050` — web ochiladi, shu parol bilan kirish mumkin.

**B. Yangilash.** O'rnatuvchini qayta ishga tushiring (xohlasangiz boshqa port bering): parol sahifasi chiqmasligi, baza va foydalanuvchilar saqlanishi, `appsettings.Production.json` dagi `Kalit` o'zgarmasligi (avval nusxalab oling), xizmat qayta ishga tushishi, eski sessiya (desktop) chiqarib yuborilmasligi kerak.

**C. Chidamlilik.**

```powershell
Stop-Process -Name FuelControl.Api -Force      # ~5 soniyada xizmat o'zi qayta ishga tushadi
Get-Service FuelControl                        # yana Running
```

Kompyuterni qayta ishga tushiring — xizmat o'zi ishga tushishi kerak (kirmasdan ham).

**D. Faqat desktop** (boshqa kompyuterda): tur "Faqat desktop", server manzili `http://<server-ip>:5050` — o'rnatuvchi serverni tekshiradi; kirish ishlashi kerak. Serverda xavfsizlik devori qoidasi bo'lmasa — ulanish bo'lmaydi (A bandini tekshiring).

**E. Cloudflare Tunnel** (ixtiyoriy): 11-bo'lim; `Get-Service Cloudflared` — Running; Public Hostname ulangach `https://<domen>/openapi/v1.json`.

**F. O'chirish.** Dasturni o'chiring (Sozlamalar → Ilovalar). Keyin: `Get-Service FuelControl` "Cannot find any service" deyishi, `netsh advfirewall firewall show rule name="FuelControl API"` "No rules match the specified criteria" deyishi kerak; `C:\Program Files\FuelControl` yo'q bo'lishi, `%ProgramData%\FuelControl\` (baza, zaxira, jurnal) esa **qolishi** kerak.

Muammo bo'lsa: o'rnatuvchini `/LOG="C:\Temp\setup.log"` bilan ishga tushiring va `%ProgramData%\FuelControl\logs\` ni ko'ring.

## 17. O'rnatuvchini yig'ish (dasturchilar uchun)

```powershell
deploy\installer\build.ps1                      # to'liq: web -> publish -> cloudflared -> Inno Setup
deploy\installer\build.ps1 -SkipWeb             # web allaqachon build qilingan bo'lsa
deploy\installer\build.ps1 -SkipCloudflared     # internetsiz / Tunnel komponentisiz
```

Natija: `deploy\installer\Output\FuelControl-Setup-<versiya>.exe`. Talablar: .NET SDK 10, Node.js (Angular 22 uchun `.node-version`), **Inno Setup 7** (`winget install --id JRSoftware.InnoSetup.7 -e`) yoki Inno Setup 6.7+. Skript ikkalasida ham kompilyatsiya bo'ladi; `ISCC.exe` yo'lini `-IsccPath` yoki `ISCC` muhit o'zgaruvchisi bilan berish mumkin.

- **Versiya** yagona manbadan: repo ildizidagi `Directory.Build.props` → `<Version>`. API, Desktop, fayl nomi va o'rnatuvchi versiyasi shundan olinadi (web `package.json` `version` ni ham mos qiling).
- `build.ps1` ishchi papkadagi (commit qilinmagan o'zgarishlar bilan) kodni yig'adi. Reliz uchun toza `main` dan yig'ing.
- Publish natijalari `deploy\installer\build\artifacts` ga tushadi (ishlab turgan dev API/Desktop bilan to'qnashmaydi). `build\` va `Output\` git'ga kiritilmaydi.
- `cloudflared.exe` GitHub reliz'idan yuklanadi, **Cloudflare imzosi tekshiriladi** va repo'ga qo'shilmaydi.
- Uzbek tarjima: `Languages\Uzbek.isl` — Inno Setup "Unofficial" tarjimalaridan o'zgartirishsiz nusxa (muallif: Shamsiddinov Zafar). Eski tarjima: ayrim kam uchraydigan xabarlar inglizcha chiqadi (kompilyator ogohlantirishlari shu haqida).
- Dev konfiguratsiyalar (`appsettings.Development.json`, `appsettings.Web.json`) publish'ga va o'rnatuvchiga **kirmaydi** (`build.ps1` buni tekshiradi).

**Avtomatik sinov (administratorsiz):**

```powershell
deploy\installer\test-installer.ps1              # Output\FuelControl-Setup-<versiya>.exe ni sinaydi (~1 daqiqa)
deploy\installer\test-installer.ps1 -SkipTunnel  # o'rnatuvchi -SkipCloudflared bilan yig'ilgan bo'lsa
```

Skript o'rnatuvchini `/CURRENTUSER /NOSYSTEM=1` bilan vaqtinchalik papkaga o'rnatadi (Windows xizmati, xavfsizlik devori, ruxsatlar va cloudflared xizmatiga tegmaydi). Tekshiriladi: noto'g'ri qiymatlarni rad etish, birinchi o'rnatish, haqiqiy publish qilingan API'ni Production rejimida ishga tushirish (`/openapi`, PWA, admin login, jurnal), yangilash (JWT kaliti va `appsettings.Local.json` saqlanishi), faqat desktop, tunnel komponenti va o'chirish (ma'lumotlar saqlanishi). Xizmat, xavfsizlik devori va UAC esa 16-bo'limdagi yo'riqnoma bilan qo'lda sinaladi. Bu mashinada FuelControl allaqachon o'rnatilgan bo'lsa, skript to'xtaydi (o'rnatilishni buzmaslik uchun).
