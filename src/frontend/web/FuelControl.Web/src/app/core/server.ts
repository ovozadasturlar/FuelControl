import { Injectable } from '@angular/core';
import { api, ol } from '../api/api';
import type {
  AparatDto, AparatTahrirlashDto, AparatYaratishDto, AuditYozuviDto, BakKirimDto, BakKirimYaratishDto, BoshqaruvDto, FoydalanuvchiDto,
  EskiQarzYaratishDto, FoydalanuvchiTahrirlashDto, HisobotDto, HisobotGuruhi, KorsatkichTuzatishDto, LoginJavobi, NarxTarixiDto, NasiyaDto, NasiyalarDto,
  NasiyaQaytishiYaratishDto, MijozTaklifDto, NasiyaTafsilotDto, NasiyaYaratishDto, OperatorHisobDto, Rol, Ruxsat, SmenaDto, SmenaOchishDto, SmenaTafsilotDto,
  SmenaTopshirishDto, SmenaYopishDto, XarajatDto, XarajatYaratishDto, YoqilgiTahrirlashDto, YoqilgiTuriDto, ZaxiraHolatiDto, ZaxiraJavobi, ZaxiraTelegramTahrirDto,
} from '../api/model';

/**
 * Server bilan yagona aloqa fasadi (docs/smena-hisobi-topshiriq.md §4): komponentlar API'ni faqat shu orqali chaqiradi.
 * Tiplar generatsiya qilingan sxemadan (`npm run api`); real vaqt hodisalarini SignalR (`Aloqa`) beradi.
 */
@Injectable({ providedIn: 'root' })
export class Server {
  // ---- Kirish
  kirish(login: string, parol: string): Promise<LoginJavobi> { return ol(api.POST('/auth/login', { body: { login, parolYokiPin: parol } })); }
  men(): Promise<FoydalanuvchiDto> { return ol(api.GET('/me')); }

  // ---- Smena
  async joriySmena(): Promise<SmenaTafsilotDto | null> { return (await ol(api.GET('/smenalar/joriy'))) ?? null; }
  smena(id: number): Promise<SmenaTafsilotDto> { return ol(api.GET('/smenalar/{id}', { params: { path: { id } } })); }
  smenalar(dan?: string, gacha?: string, operatorId?: number): Promise<SmenaDto[]> {
    return ol(api.GET('/smenalar', { params: { query: { dan, gacha, operatorId } } }));
  }
  /** To'liq natija (faqat `Smenalar` ruxsati yoki shu smenaning operatori; boshqalarga 403 — chaqirilmaydi, docs §8.10). */
  async oxirgiYopilgan(): Promise<SmenaTafsilotDto | null> { return (await ol(api.GET('/smenalar/oxirgi'))) ?? null; }
  /** Keyingi operator uchun oldingi smena topshiruvi (natijasiz); yopilgan smena yo'q bo'lsa null (204). */
  async topshirish(): Promise<SmenaTopshirishDto | null> {
    return (await ol(api.GET('/smenalar/oxirgi/topshirish'))) ?? null;
  }
  smenaOch(d: SmenaOchishDto): Promise<SmenaDto> { return ol(api.POST('/smenalar/och', { body: d })); }
  smenaYop(id: number, d: SmenaYopishDto): Promise<SmenaDto> { return ol(api.POST('/smenalar/{id}/yop', { params: { path: { id } }, body: d })); }
  korsatkichTuzat(id: number, d: KorsatkichTuzatishDto): Promise<SmenaTafsilotDto> {
    return ol(api.PUT('/smenalar/{id}/korsatkich', { params: { path: { id } }, body: d }));
  }

  /** Ochiq smenada narx o'zgarishida yozilgan ko'rsatkichni tuzatish (docs §8.14): aparatning shu smenadagi oxirgi segmenti; `qiymat` segment boshiga teng bo'lsa segment o'chadi. */
  narxKorsatkichTuzat(id: number, d: KorsatkichTuzatishDto): Promise<SmenaTafsilotDto> {
    return ol(api.PUT('/smenalar/{id}/narx-korsatkichi', { params: { path: { id } }, body: d }));
  }

  // ---- Nasiya
  /** `operatorId` berilsa server shu operator yozgan nasiyalarni qaytaradi va `operator` xulosasini to'ldiradi (docs §8.12). */
  nasiyalar(holat?: 'faol' | 'otgan' | 'yopilgan', q?: string, operatorId?: number): Promise<NasiyalarDto> {
    return ol(api.GET('/nasiyalar', { params: { query: { holat, q: q?.trim() || undefined, operatorId } } }));
  }
  /** Mavjud mijozlar (nasiya yozuvlaridan): ism, telefon yoki mashina raqami bo'yicha, eng ko'pi 8 ta; `q` bo'sh bo'lsa — oxirgi mijozlar. */
  mijozlar(q: string): Promise<MijozTaklifDto[]> { return ol(api.GET('/nasiyalar/mijozlar', { params: { query: { q: q.trim() || undefined } } })); }
  nasiya(id: number): Promise<NasiyaTafsilotDto> { return ol(api.GET('/nasiyalar/{id}', { params: { path: { id } } })); }
  nasiyaYarat(d: NasiyaYaratishDto): Promise<NasiyaDto> { return ol(api.POST('/nasiyalar', { body: d })); }
  nasiyaQaytishi(id: number, d: NasiyaQaytishiYaratishDto): Promise<NasiyaDto> {
    return ol(api.POST('/nasiyalar/{id}/qaytish', { params: { path: { id } }, body: d }));
  }
  /** Dasturdan oldingi qarz to'lovi (docs §8.11): ochiq smena kerak, aks holda 409. */
  eskiQarz(d: EskiQarzYaratishDto): Promise<NasiyaTafsilotDto> { return ol(api.POST('/nasiyalar/eski-qarz', { body: d })); }
  async nasiyaOchir(id: number): Promise<void> { await ol(api.DELETE('/nasiyalar/{id}', { params: { path: { id } } })); }
  async qaytishOchir(id: number): Promise<void> { await ol(api.DELETE('/nasiyalar/qaytishlar/{id}', { params: { path: { id } } })); }

  // ---- Xarajat
  xarajatYarat(d: XarajatYaratishDto): Promise<XarajatDto> { return ol(api.POST('/xarajatlar', { body: d })); }
  async xarajatOchir(id: number): Promise<void> { await ol(api.DELETE('/xarajatlar/{id}', { params: { path: { id } } })); }

  // ---- Aparat / bak / yoqilg'i
  async aparatlar(): Promise<AparatDto[]> { return [...(await ol(api.GET('/aparatlar')))].sort((a, b) => a.raqam - b.raqam); }
  bakKirim(aparatId: number, d: BakKirimYaratishDto): Promise<AparatDto> { return ol(api.POST('/aparatlar/{id}/kirim', { params: { path: { id: aparatId } }, body: d })); }
  bakKirimlar(aparatId?: number): Promise<BakKirimDto[]> { return ol(api.GET('/bak-kirimlar', { params: { query: { aparatId } } })); }
  aparatYarat(d: AparatYaratishDto): Promise<AparatDto> { return ol(api.POST('/aparatlar', { body: d })); }
  aparatTahrirla(id: number, d: AparatTahrirlashDto): Promise<AparatDto> { return ol(api.PUT('/aparatlar/{id}', { params: { path: { id } }, body: d })); }
  yoqilgilar(): Promise<YoqilgiTuriDto[]> { return ol(api.GET('/yoqilgilar')); }
  yoqilgiYarat(nomi: string, narx: number, rang: string): Promise<YoqilgiTuriDto> { return ol(api.POST('/yoqilgilar', { body: { nomi, narx, rang } })); }
  yoqilgiTahrirla(id: number, d: YoqilgiTahrirlashDto): Promise<YoqilgiTuriDto> { return ol(api.PUT('/yoqilgilar/{id}', { params: { path: { id } }, body: d })); }
  async yoqilgiOchir(id: number): Promise<void> { await ol(api.DELETE('/yoqilgilar/{id}', { params: { path: { id } } })); }
  narxTarixi(): Promise<NarxTarixiDto[]> { return ol(api.GET('/yoqilgilar/narx-tarixi')); }

  // ---- Hisobot / boshqaruv / audit / operator
  hisobot(dan: string | undefined, gacha: string | undefined, operatorId: number | undefined, guruh: HisobotGuruhi = 'Smena'): Promise<HisobotDto> {
    return ol(api.GET('/hisobot', { params: { query: { dan, gacha, operatorId, guruh: guruh.toLowerCase() } } }));
  }
  boshqaruv(): Promise<BoshqaruvDto> { return ol(api.GET('/boshqaruv')); }
  audit(q?: string, tur?: string): Promise<AuditYozuviDto[]> { return ol(api.GET('/audit', { params: { query: { q: q?.trim() || undefined, tur: tur || undefined } } })); }
  /** Fayl eksporti auditga yoziladi (desktop bilan bir xil turi/tafsilot). Eksport allaqachon bajarilgan — audit xatosi jim o'tadi. */
  async auditEksport(turi: string, tafsilot: string): Promise<void> {
    try { await ol(api.POST('/audit/eksport', { body: { turi, tafsilot } })); } catch { /* audit yozilmadi — foydalanuvchiga xalaqit bermaymiz */ }
  }
  operatorlar(): Promise<FoydalanuvchiDto[]> { return ol(api.GET('/operatorlar')); }
  operatorHisob(id: number, oy?: string): Promise<OperatorHisobDto> { return ol(api.GET('/operatorlar/{id}/hisob', { params: { path: { id }, query: { oy } } })); }
  async avansBer(id: number, summa: number, turi: 'Avans' | 'Tolov', izoh: string): Promise<void> {
    // Summa musbat yuboriladi — server Avans/To'lov uchun hisobdan ayirib (manfiy) yozadi.
    await ol(api.POST('/operatorlar/{id}/harakat', { params: { path: { id } }, body: { turi, summa: Math.abs(summa), izoh } }));
  }

  // ---- Foydalanuvchi / ruxsat / zaxira
  foydalanuvchilar(): Promise<FoydalanuvchiDto[]> { return ol(api.GET('/foydalanuvchilar')); }
  foydalanuvchiYarat(toliqIsm: string, login: string, rol: Rol, oylikMaosh: number, parol: string): Promise<FoydalanuvchiDto> {
    return ol(api.POST('/foydalanuvchilar', { body: { toliqIsm, login, rol, oylikMaosh, parolYokiPin: parol } }));
  }
  async foydalanuvchiTahrirla(id: number, d: FoydalanuvchiTahrirlashDto): Promise<void> { await ol(api.PUT('/foydalanuvchilar/{id}', { params: { path: { id } }, body: d })); }
  async pinOrnat(id: number, pin: string): Promise<void> { await ol(api.POST('/foydalanuvchilar/{id}/pin', { params: { path: { id } }, body: { yangiParolYokiPin: pin } })); }
  ruxsatlarniOrnat(id: number, ruxsatlar: Ruxsat[]): Promise<FoydalanuvchiDto> { return ol(api.PUT('/foydalanuvchilar/{id}/ruxsatlar', { params: { path: { id } }, body: { ruxsatlar } })); }
  zaxira(): Promise<ZaxiraJavobi> { return ol(api.POST('/zaxira')); }
  zaxiraHolati(): Promise<ZaxiraHolatiDto> { return ol(api.GET('/zaxira/holat')); }
  /** token / chatId: null = o'zgarmaydi, "" = o'chirish; yoqilgan: null = o'zgarmaydi. Javob — yangilangan holat. */
  zaxiraTelegramSaqla(d: ZaxiraTelegramTahrirDto): Promise<ZaxiraHolatiDto> { return ol(api.PUT('/zaxira/telegram', { body: d })); }
  /** Yuborilmasa ham 200 (oxirgiMuvaffaqiyat=false + matn); 400 faqat token yoki chat ID yo'q bo'lganda. */
  zaxiraSinov(): Promise<ZaxiraHolatiDto> { return ol(api.POST('/zaxira/telegram/sinov')); }
}
