import { Injectable, signal, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { apiSozla, ApiXato } from '../api/api';
import { Server } from './server';
import { sana } from './format';
import type { FoydalanuvchiDto, LoginJavobi, Ruxsat } from '../api/model';

const KALIT = 'fc.sessiya';

/** Muddatining yarmidan ko'pi o'tgan (12 soatlik tokenda 6 soatdan kam qolgan) token faol foydalanuvchi uchun fonda uzaytiriladi (docs §8.18). */
const UZAYTIRISH_CHEGARASI_MS = 6 * 3600e3;
/**
 * Faol foydalanuvchi: oxirgi shuncha vaqt ichida klaviatura/sichqoncha/sensor harakati bo'lgan. Foydalanuvchi boshlagan so'rov har doim shu hodisalardan biri bilan boshlanadi;
 * SignalR, taymer va davriy tekshiruvlar yuborgan fon so'rovlari faollik HISOBLANMAYDI (aks holda ochiq qolgan kompyuterda token cheksiz uzayardi).
 */
const FAOLLIK_MS = 30 * 60e3;
const TEKSHIRISH_ORALIGI_MS = 10 * 60e3;
/** Server soati bilan kichik farq bo'lishi mumkin (ClockSkew 1 daqiqa): bundan kam qolgan token fonda uzaytirilmaydi — 401 kelsa modal chiqadi. */
const XAVFSIZ_QOLGAN_MS = 60e3;

interface Sessiya {
  token: string;
  muddati: string;
  foydalanuvchi: FoydalanuvchiDto;
  /** Token berilgan vaqt (klient soati, ms): uzaytirish chegarasi token umriga qarab hisoblanadi (qisqa tokenda ham to'g'ri ishlasin). */
  berilgan?: number;
}

/**
 * JWT sessiyasi. "Eslab qolish" — localStorage (12 soatgacha), aks holda sessionStorage (tab yopilguncha).
 * Ishlab turgan foydalanuvchi chiqarib yuborilmaydi (docs §8.18): token fonda uzaytiriladi (`/auth/yangila`), muddat baribir tugasa
 * (401) kirish sahifasiga otilmaydi — joriy ekran ustida parol/PIN so'raladi va rad etilgan so'rov qayta yuboriladi.
 */
@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly router = inject(Router);
  private readonly server = inject(Server);
  readonly sessiya = signal<Sessiya | null>(this.oqi());
  readonly foydalanuvchi = computed(() => this.sessiya()?.foydalanuvchi ?? null);
  readonly kirganmi = computed(() => this.sessiya() !== null);
  /** To'liq chiqarilganda (nofaol/bloklangan va h.k.) login sahifasida xabar ko'rsatish uchun. */
  readonly tugadi = signal(false);
  /** 401: "Sessiya tugadi — parol/PIN ni qayta kiriting" oynasi ochiq (ekran va dialoglar joyida turadi). */
  readonly qaytaKirishKerak = signal(false);
  private readonly ruxsatlar = computed(() => new Set(this.foydalanuvchi()?.ruxsatlar ?? []));

  private oxirgiFaollik = Date.now();
  private uzaytirilmoqda = false;
  private tugadiVada: Promise<string | null> | null = null;
  private tugadiHal: ((token: string | null) => void) | null = null;

  constructor() {
    apiSozla({ token: () => this.token(), oldin: () => this.oldin(), tugadi: (t) => this.tugadi401(t) });
    for (const h of ['pointerdown', 'pointermove', 'keydown', 'wheel', 'touchstart']) {
      addEventListener(h, () => { this.oxirgiFaollik = Date.now(); }, { passive: true, capture: true });
    }
    setInterval(() => this.uzaytirishniTekshir(), TEKSHIRISH_ORALIGI_MS);
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') this.uzaytirishniTekshir(); });
    // Boshqa tab tokenni uzaytirsa (yoki qayta kirsa) — yangi tokenni shu tab ham oladi.
    addEventListener('storage', (e) => this.boshqaTab(e));
  }

  /** Joriy token. Muddati o'tgan bo'lsa ham beriladi: server 401 qaytarsa "Sessiya tugadi" oynasi chiqadi (chiqarib yuborilmaydi). */
  token(): string | null {
    return this.sessiya()?.token ?? null;
  }

  bor(r: Ruxsat): boolean {
    return this.ruxsatlar().has(r);
  }

  async kirish(login: string, parolYokiPin: string, eslab: boolean) {
    const j = await this.server.kirish(login, parolYokiPin);
    const s = this.yangiSessiya(j);
    this.yoz(s, eslab);
    this.tugadi.set(false);
    this.sessiya.set(s);
  }

  /** Ruxsatlar o'zgargan bo'lishi mumkin — /me dan yangilaymiz. */
  async yangila() {
    const s = this.sessiya();
    if (!s) return;
    try {
      const f = await this.server.men();
      const yangi = { ...s, foydalanuvchi: f };
      this.yoz(yangi, localStorage.getItem(KALIT) !== null);
      this.sessiya.set(yangi);
    } catch (e) {
      if (!(e instanceof ApiXato)) return; // aloqa yo'q — eski ma'lumot bilan ishlaymiz
    }
  }

  chiqish(muddatTugadi = false) {
    this.tugadiYop(null);
    if (!this.sessiya()) return;
    try { localStorage.removeItem(KALIT); sessionStorage.removeItem(KALIT); } catch { /* */ }
    this.sessiya.set(null);
    this.tugadi.set(muddatTugadi);
    this.router.navigateByUrl('/kirish');
  }

  /**
   * "Sessiya tugadi" oynasidan: login o'zgarmaydi, faqat parol/PIN. Muvaffaqiyatli bo'lsa oyna yopiladi va kutayotgan so'rovlar yangi token bilan qayta yuboriladi.
   * Xato (401 noto'g'ri parol, 403, 429) chaqiruvchiga uzatiladi.
   */
  async qaytaKirish(parolYokiPin: string) {
    const s = this.sessiya();
    if (!s) return;
    const j = await this.server.kirish(s.foydalanuvchi.login, parolYokiPin);
    this.almashtir(j);
    this.tugadiYop(j.token);
  }

  // ---- Token uzaytirish (fon)

  /** Har API so'rovidan oldin uzaytirish kerakmi tekshiriladi. So'rovning o'zi faollik hisoblanmaydi (fon so'rovlari bo'lishi mumkin). */
  private oldin() {
    this.uzaytirishniTekshir();
  }

  private uzaytirishniTekshir() {
    const s = this.sessiya();
    if (!s || this.uzaytirilmoqda || this.qaytaKirishKerak()) return;
    const qolgan = sana(s.muddati).getTime() - Date.now();
    if (qolgan <= XAVFSIZ_QOLGAN_MS || qolgan >= this.chegara(s)) return;
    if (Date.now() - this.oxirgiFaollik > FAOLLIK_MS) return;
    void this.uzaytir();
  }

  private chegara(s: Sessiya): number {
    const umr = s.berilgan ? sana(s.muddati).getTime() - s.berilgan : 0;
    return umr > 0 ? Math.min(UZAYTIRISH_CHEGARASI_MS, umr / 2) : UZAYTIRISH_CHEGARASI_MS;
  }

  /** Bir vaqtda faqat bitta so'rov (`uzaytirilmoqda`). Aloqa yo'q bo'lsa jim: keyingi tekshiruvda qayta uriniladi. */
  private async uzaytir() {
    this.uzaytirilmoqda = true;
    try {
      this.almashtir(await this.server.authYangila());
    } catch (e) {
      // Amaldagi token bilan 401/403 — foydalanuvchi nofaol yoki bloklangan: avvalgidek to'liq chiqiladi.
      if (e instanceof ApiXato && (e.status === 401 || e.status === 403)) this.chiqish(true);
    } finally {
      this.uzaytirilmoqda = false;
    }
  }

  /** Yangi token: xotiradagi sessiya va hozir saqlanayotgan joy (localStorage/sessionStorage) yangilanadi; boshqa tablar 'storage' orqali oladi. */
  private almashtir(j: LoginJavobi) {
    const s = this.yangiSessiya(j);
    try {
      const joy = localStorage.getItem(KALIT) !== null ? localStorage : sessionStorage.getItem(KALIT) !== null ? sessionStorage : null;
      joy?.setItem(KALIT, JSON.stringify(s)); // boshqa tabda chiqilgan bo'lsa (kalit yo'q) — qayta yozib "tiriltirmaymiz"
    } catch { /* */ }
    this.sessiya.set(s);
  }

  private yangiSessiya(j: LoginJavobi): Sessiya {
    return { token: j.token, muddati: j.tokenMuddati, foydalanuvchi: j.foydalanuvchi, berilgan: Date.now() };
  }

  private boshqaTab(e: StorageEvent) {
    if (e.storageArea !== localStorage || e.key !== KALIT || !e.newValue) return;
    const hozir = this.sessiya();
    const s = this.oqi();
    if (!hozir || !s || s.foydalanuvchi.id !== hozir.foydalanuvchi.id || s.token === hozir.token) return;
    if (sana(s.muddati).getTime() <= sana(hozir.muddati).getTime()) return;
    this.sessiya.set(s);
    if (this.tugadiHal) this.tugadiYop(s.token); // boshqa tabda qayta kirilgan — bu tabdagi oyna ham yopiladi
  }

  // ---- 401: "Sessiya tugadi"

  private tugadi401(ishlatilgan: string | null): Promise<string | null> {
    if (!this.sessiya()) return Promise.resolve(null);
    // So'rov ketgandan keyin token almashgan bo'lishi mumkin (boshqa tab yoki fon uzaytirish): yangisi yaroqli bo'lsa, oynasiz qayta yuboriladi.
    const saqlangan = this.oqi();
    if (saqlangan && saqlangan.token !== ishlatilgan && saqlangan.foydalanuvchi.id === this.sessiya()!.foydalanuvchi.id) {
      this.sessiya.set(saqlangan);
      return Promise.resolve(saqlangan.token);
    }
    const joriy = this.sessiya()!;
    if (joriy.token !== ishlatilgan && sana(joriy.muddati).getTime() > Date.now()) return Promise.resolve(joriy.token);
    if (!this.tugadiVada) {
      this.qaytaKirishKerak.set(true);
      this.tugadiVada = new Promise((hal) => { this.tugadiHal = hal; });
    }
    return this.tugadiVada;
  }

  private tugadiYop(token: string | null) {
    const hal = this.tugadiHal;
    this.tugadiHal = null;
    this.tugadiVada = null;
    this.qaytaKirishKerak.set(false);
    hal?.(token);
  }

  private yoz(s: Sessiya, eslab: boolean) {
    try {
      const maqsad = eslab ? localStorage : sessionStorage;
      (eslab ? sessionStorage : localStorage).removeItem(KALIT);
      maqsad.setItem(KALIT, JSON.stringify(s));
    } catch { /* */ }
  }

  private oqi(): Sessiya | null {
    try {
      const m = localStorage.getItem(KALIT) ?? sessionStorage.getItem(KALIT);
      if (!m) return null;
      const s = JSON.parse(m) as Sessiya;
      return sana(s.muddati).getTime() > Date.now() ? s : null;
    } catch {
      return null;
    }
  }
}
