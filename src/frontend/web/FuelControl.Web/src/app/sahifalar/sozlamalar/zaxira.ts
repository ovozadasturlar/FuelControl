import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Til } from '../../core/til';
import { Server } from '../../core/server';
import { Bildirish, xatoMatni } from '../../core/bildirish';
import { jonliYangila } from '../../core/malumot';
import { kun, pul, soat } from '../../core/format';
import type { ZaxiraHolatiDto, ZaxiraNusxasiDto } from '../../api/model';
import { Ikon } from '../../ui/ikon';

/** Eski versiyada oxirgi nusxa brauzerda saqlanardi; endi u serverdan keladi. */
const ESKI_KALIT = 'fc.oxirgiNusxa';

/**
 * "Zaxira nusxa" (docs §8.17, desktop bilan bir xil): papka yo'li (standartga qaytsa sariq ogohlantirish), "Hozir nusxa olish",
 * oxirgi nusxalar ro'yxati va Telegram sozlamalari (maskalangan token, chat ID, "Yoqilgan", "Sinov yuborish", oxirgi yuborish holati).
 * Token klientga to'liq qaytmaydi: maydon bo'sh turadi, maska placeholder'da; bo'sh qoldirilsa o'zgarmaydi ("" faqat "Tokenni o'chirish" yuboradi).
 */
@Component({
  selector: 'sozlamalar-zaxira',
  imports: [FormsModule, Ikon],
  template: `
    @if (h(); as z) {
      <div class="zaxira-ustun">
        <section class="shisha karta zaxira" aria-labelledby="zx-sarlavha">
          <h2 id="zx-sarlavha">{{ til.t('ZaxiraNusxa') }}</h2>
          <div class="plitka papka">
            <div class="yorliq">{{ til.t('Zaxira_Papka') }}</div>
            <div class="yol" data-test="zx-papka">{{ z.papka }}</div>
            <div class="ikkilamchi kichik-matn" data-test="zx-qachon">{{ til.t('Zaxira_Qachon', z.saqlash) }}</div>
            <div class="ikkilamchi kichik-matn server-izoh" data-test="zx-papka-server">{{ til.t('Zaxira_PapkaServerda', z.papka) }}</div>
          </div>
          @if (ogoh(); as o) {
            <div class="ogohlik-blok" role="status" data-test="zx-standart">
              <ikon nomi="warning" [olcham]="18" [qalinlik]="2" />
              <div class="ogohlik-matn">
                <span data-test="zx-ogoh-asosiy">{{ o.asosiy }}</span>
                @if (o.kichik) { <span class="sabab-matn" data-test="zx-standart-sabab">{{ o.kichik }}</span> }
              </div>
            </div>
          }
          <div class="plitka oxirgi">
            <div class="bosh-joy">
              <div class="qalin">{{ til.t('OxirgiNusxa') }}</div>
              <div class="ikkilamchi kichik-matn matn" data-test="zx-oxirgi">{{ oxirgiMatn() }}</div>
            </div>
            <div class="oxirgi-tugmalar">
              <button type="button" class="tugma" data-test="zx-tiklash" [attr.aria-expanded]="tiklashOchiq()" (click)="tiklashOchiq.set(!tiklashOchiq())">{{ til.t('NusxadanTiklash') }}</button>
              <button type="button" class="tugma asosiy" data-test="zx-hozir" [disabled]="band()" (click)="olish()">
                @if (band()) { <span class="aylanma"></span> } {{ til.t('HozirNusxa') }}
              </button>
            </div>
          </div>
          @if (tiklashOchiq()) {
            <div class="malumot-blok" role="status" data-test="zx-tiklash-izoh"><ikon nomi="info" [olcham]="18" [qalinlik]="2" /><span>{{ til.t('Zaxira_TiklashIzoh', z.papka) }}</span></div>
          }

          <h3 class="kichik-sarlavha">{{ til.t('Zaxira_OxirgiNusxalar') }}</h3>
          @if (z.nusxalar.length === 0) {
            <p class="ikkilamchi kichik-matn" data-test="zx-bosh">{{ til.t('Zaxira_NusxaYoq') }}</p>
          } @else {
            <div class="nusxalar" role="table" data-test="zx-nusxalar">
              <div class="nusxa bosh-qator" role="row">
                <span role="columnheader">{{ til.t('Zaxira_Fayl') }}</span>
                <span role="columnheader">{{ til.t('Zaxira_Vaqti') }}</span>
                <span role="columnheader" class="o">{{ til.t('Zaxira_Hajmi') }}</span>
                <span role="columnheader">{{ til.t('Zaxira_Sababi') }}</span>
              </div>
              @for (n of z.nusxalar; track n.nomi) {
                <div class="nusxa" role="row" data-test="zx-nusxa">
                  <span role="cell" class="fayl">{{ n.nomi }} @if (n.standartPapkada) { <span class="standart-belgi" data-test="zx-standart-belgi">{{ til.t('Zaxira_StandartPapkada') }}</span> }</span>
                  <span role="cell" class="vaqt">{{ vaqtMatni(n.vaqt) }}</span>
                  <span role="cell" class="hajm">{{ hajm(n.hajmBayt) }}</span>
                  <span role="cell" class="sabab">{{ sabab(n) }}</span>
                </div>
              }
            </div>
          }
        </section>

        <section class="shisha karta telegram" aria-labelledby="zx-tg-sarlavha">
          <h2 id="zx-tg-sarlavha">{{ til.t('Zaxira_Telegram') }}</h2>
          <p class="ikkilamchi kichik-matn">{{ til.t('Zaxira_TelegramIzoh') }}</p>
          <form class="forma-ustun" (ngSubmit)="saqla()" autocomplete="off" novalidate data-test="zx-forma">
            <div class="maydon">
              <label for="zx-token">{{ til.t('Zaxira_Token') }}</label>
              <input id="zx-token" class="kiritish token-in" name="zxToken" [(ngModel)]="token" [placeholder]="tokenIzoh()"
                     [class.xato]="!!xatoToken()" autocomplete="off" autocapitalize="off" autocorrect="off" spellcheck="false" />
              <span class="yordam-matn">{{ til.t('Zaxira_TokenYangi') }}</span>
              @if (xatoToken(); as m) { <span class="xato-matn" role="alert" data-test="zx-xato-token">{{ m }}</span> }
              @if (z.telegram.tokenMaska && !tokenSurov()) {
                <button type="button" class="havola token-ochir" data-test="zx-token-ochir" [disabled]="saqlanmoqda()" (click)="tokenSurov.set(true)">{{ til.t('Zaxira_TokenOchir') }}</button>
              }
            </div>
            @if (tokenSurov()) {
              <div class="ogohlik-blok surov" role="alert" data-test="zx-token-surov">
                <ikon nomi="warning" [olcham]="16" [qalinlik]="2" />
                <span class="bosh-joy">{{ til.t('Zaxira_TokenOchirSavol') }}</span>
                <button type="button" class="tugma kichik" data-test="zx-token-bekor" (click)="tokenSurov.set(false)">{{ til.t('BekorQilish') }}</button>
                <button type="button" class="tugma kichik xavfli toliq" data-test="zx-token-tasdiq" [disabled]="saqlanmoqda()" (click)="tokenOchir()">
                  @if (saqlanmoqda()) { <span class="aylanma"></span> } {{ til.t('Ochirish') }}
                </button>
              </div>
            }
            <div class="maydon">
              <label for="zx-chat">{{ til.t('Zaxira_ChatId') }}</label>
              <input id="zx-chat" class="kiritish" name="zxChat" [(ngModel)]="chatId" [class.xato]="!!xatoChat()" autocomplete="off" autocapitalize="off" spellcheck="false" />
              @if (xatoChat(); as m) { <span class="xato-matn" role="alert" data-test="zx-xato-chat">{{ m }}</span> }
            </div>
            <label class="belgi"><input id="zx-yoqilgan" type="checkbox" name="zxYoqilgan" [(ngModel)]="yoqilgan" /> {{ til.t('Zaxira_Yoqilgan') }}</label>

            <div class="plitka holat" data-test="zx-holat" [class.ok]="holat().tur === 'ok'" [class.xato]="holat().tur === 'xato'">
              <div class="yorliq">{{ til.t('Zaxira_OxirgiYuborish') }}</div>
              <div class="holat-matn" data-test="zx-holat-matn">{{ holat().matn }}</div>
              @if (holat().kutilmoqda) { <div class="kutilmoqda" data-test="zx-kutilmoqda">{{ til.t('Zaxira_Kutilmoqda') }}</div> }
              @if (faolIzoh(); as f) {
                <div [class]="f.neytral ? 'ikkilamchi kichik-matn' : 'xato-matn'" [attr.data-neytral]="f.neytral" data-test="zx-faol-xato">{{ f.matn }}</div>
              }
            </div>

            @if (xato()) { <div class="xato-matn" role="alert" data-test="zx-xato">{{ xato() }}</div> }
            <div class="amallar">
              @if (ozgardi()) { <span class="saqlanmagan" data-test="zx-saqlanmagan">{{ til.t('Zaxira_Saqlanmagan') }}</span> }
              <button type="button" class="tugma" data-test="zx-sinov" [disabled]="saqlanmoqda() || sinovBand()" (click)="sinov()">
                @if (sinovBand()) { <span class="aylanma"></span> } @else { <ikon nomi="share" [olcham]="16" [qalinlik]="2.2" /> } {{ til.t('Zaxira_SinovYuborish') }}
              </button>
              <button type="submit" class="tugma asosiy" data-test="zx-saqla" [disabled]="saqlanmoqda() || sinovBand() || !ozgardi()">
                @if (saqlanmoqda()) { <span class="aylanma"></span> } @else { <ikon nomi="check" [olcham]="16" [qalinlik]="2.4" /> } {{ til.t('Saqlash') }}
              </button>
            </div>
          </form>
        </section>
      </div>
    } @else if (yuklashXato()) {
      <div class="shisha karta bosh">
        {{ yuklashXato() }}<br /><br />
        <button type="button" class="tugma" (click)="yukla()"><ikon nomi="refresh" [olcham]="16" /> {{ til.t('Yangilash') }}</button>
      </div>
    } @else {
      <div class="shisha karta"><div class="skelet" style="height:180px"></div></div>
    }
  `,
  styles: `
    .zaxira-ustun { display: flex; flex-direction: column; gap: 16px; max-width: 760px; }
    .zaxira h2, .telegram h2 { font-size: 16px; }
    .zaxira, .telegram { display: flex; flex-direction: column; gap: 14px; }
    .yorliq { font-size: 11px; font-weight: 700; letter-spacing: 1px; text-transform: uppercase; color: var(--matn-2); }
    .yol { margin: 4px 0 6px; font-weight: 700; font-variant-numeric: tabular-nums; overflow-wrap: anywhere; }
    .ogohlik-matn { display: flex; flex-direction: column; gap: 4px; min-width: 0; overflow-wrap: anywhere; font-weight: 600; }
    .ogohlik-blok ikon { flex: none; margin-top: 1px; }
    .sabab-matn { font-weight: 500; font-size: 12.5px; opacity: 0.9; }
    .oxirgi { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; }
    .oxirgi .bosh-joy { flex: 1 1 220px; min-width: 0; }
    .matn { overflow-wrap: anywhere; }
    .kichik-sarlavha { margin: 4px 0 -4px; font-size: 14px; font-weight: 800; }

    .nusxalar { container-type: inline-size; display: flex; flex-direction: column; }
    .nusxa { display: grid; grid-template-columns: minmax(0, 2.4fr) minmax(104px, 1fr) minmax(64px, .7fr) minmax(0, 1.4fr); gap: 4px 12px; align-items: baseline; padding: 10px 2px; border-top: 1px solid var(--plitka-chegara); font-size: 13.5px; }
    .nusxa.bosh-qator { border-top: 0; padding-top: 0; font-size: 11px; font-weight: 700; letter-spacing: 1px; text-transform: uppercase; color: var(--matn-2); }
    .nusxa .o, .nusxa .hajm { text-align: right; }
    .nusxa .fayl { font-weight: 700; overflow-wrap: anywhere; }
    .nusxa .vaqt, .nusxa .hajm { font-variant-numeric: tabular-nums; white-space: nowrap; }
    .nusxa .sabab { color: var(--matn-2); overflow-wrap: anywhere; }
    @container (max-width: 520px) {
      .nusxa.bosh-qator { display: none; }
      .nusxa { grid-template-columns: auto auto minmax(0, 1fr); }
      .nusxa .fayl { grid-column: 1 / -1; }
      .nusxa .hajm { text-align: left; }
    }

    .token-in { -webkit-text-security: disc; }
    .token-in::placeholder { -webkit-text-security: none; }
    .token-ochir { align-self: flex-start; margin-top: 2px; color: var(--qizil); }
    .holat .holat-matn { margin-top: 4px; font-weight: 700; overflow-wrap: anywhere; font-variant-numeric: tabular-nums; }
    .holat.ok .holat-matn { color: var(--yashil); }
    .holat.xato .holat-matn { color: var(--qizil); }
    .kutilmoqda { margin-top: 2px; font-size: 13px; font-weight: 600; color: var(--sariq); }
    .standart-belgi {
      display: inline-block; margin-left: 6px; padding: 1px 8px; border-radius: 999px; font-size: 11px; font-weight: 700; white-space: nowrap;
      color: var(--matn-2); background: var(--plitka-fon); border: 1px solid var(--plitka-chegara);
    }
    .surov { align-items: center; flex-wrap: wrap; }
    .surov .bosh-joy { min-width: 180px; }
    .amallar { display: flex; flex-wrap: wrap; align-items: center; justify-content: flex-end; gap: 10px; }
    .saqlanmagan { margin-right: auto; font-size: 12.5px; font-weight: 600; color: var(--sariq); }
    .server-izoh { margin-top: 4px; overflow-wrap: anywhere; }
    .oxirgi-tugmalar { display: flex; flex-wrap: wrap; gap: 8px; }
    .malumot-blok span { min-width: 0; overflow-wrap: anywhere; }
    @media (max-width: 480px) { .oxirgi-tugmalar { width: 100%; } .oxirgi .tugma, .amallar .tugma { flex: 1 1 100%; } }
  `,
})
export class ZaxiraBolimi {
  protected readonly til = inject(Til);
  private readonly server = inject(Server);
  private readonly bildirish = inject(Bildirish);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly h = signal<ZaxiraHolatiDto | null>(null);
  protected readonly yuklashXato = signal('');
  protected readonly band = signal(false);
  protected readonly saqlanmoqda = signal(false);
  protected readonly sinovBand = signal(false);
  /** "Tokenni o'chirish" bosilgach tasdiq so'rovi (docs §8.17). */
  protected readonly tokenSurov = signal(false);
  /** "Nusxadan tiklash…" — faqat ma'lumot: tiklash server kompyuterida administrator tomonidan bajariladi. */
  protected readonly tiklashOchiq = signal(false);
  protected readonly xato = signal('');
  /** Saqlash/Sinov bosilgandan keyin maydon xatolari ko'rinadi (bosishdan oldin qizarmaydi). */
  private readonly urindi = signal(false);
  private readonly sinovUrindi = signal(false);

  // ---- Telegram formasi. Token hech qachon serverdan kelmaydi: faqat yangisi yoziladi.
  protected readonly token = signal('');
  protected readonly chatId = signal('');
  protected readonly yoqilgan = signal(false);

  private kechikish: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    try { localStorage.removeItem(ESKI_KALIT); } catch { /* */ }
    this.destroyRef.onDestroy(() => clearTimeout(this.kechikish));
    void this.yukla();
    // Smena yopilganda server fon rejimida nusxa oladi — ro'yxat o'zi yangilanadi.
    jonliYangila(() => void this.yukla(true));
  }

  /** Forma o'zgargan (token yozilgan, chat ID yoki "Yoqilgan" boshqacha) — shunda "Saqlash" ochiladi. */
  protected readonly ozgardi = computed(() => {
    const t = this.h()?.telegram;
    if (!t) return false;
    return this.token().trim() !== '' || this.chatId().trim() !== (t.chatId ?? '') || this.yoqilgan() !== t.yoqilgan;
  });

  protected readonly tokenIzoh = computed(() => {
    const m = this.h()?.telegram.tokenMaska;
    return m ? this.til.t('Zaxira_TokenSaqlangan', m) : this.til.t('Zaxira_TokenYoq');
  });

  private tokenBor(): boolean { return !!this.h()?.telegram.tokenMaska || this.token().trim() !== ''; }
  private chatBor(): boolean { return this.chatId().trim() !== ''; }

  protected readonly xatoToken = computed(() =>
    this.urindi() && (this.yoqilgan() || this.sinovUrindi()) && !this.tokenBor() ? this.til.t('Zaxira_XatoToken') : '');
  protected readonly xatoChat = computed(() =>
    this.urindi() && (this.yoqilgan() || this.sinovUrindi()) && !this.chatBor() ? this.til.t('Zaxira_XatoChatId') : '');

  /** Sariq ogohlantirish (desktop bilan bir xil): standartga qaytgan bo'lsa "papka + sabab (kichik)"; qaytmagan, lekin sabab bor bo'lsa — faqat sabab. */
  protected readonly ogoh = computed<{ asosiy: string; kichik: string } | null>(() => {
    const z = this.h();
    if (!z) return null;
    const sabab = z.standartPapkaSababi?.trim() ?? '';
    if (z.standartPapkaga) return { asosiy: this.til.t('Zaxira_PapkaStandart', z.sozlanganPapka), kichik: sabab };
    return sabab ? { asosiy: sabab, kichik: '' } : null;
  });

  /** Oxirgi yuborish: "Bugun 14:02 · nusxa · yuborildi" yoki "… · nusxa · xato: matn". "Kutilmoqda" — faol va oxirgi NUSXA yuborilmagan bo'lsa. */
  protected readonly holat = computed<{ tur: 'yoq' | 'ok' | 'xato'; matn: string; kutilmoqda: boolean }>(() => {
    const t = this.h()?.telegram;
    if (!t || !t.oxirgiVaqt) return { tur: 'yoq', matn: this.til.t('Zaxira_YuborishYoq'), kutilmoqda: false };
    const v = `${this.vaqtMatni(t.oxirgiVaqt)} · ${this.til.t(t.oxirgiTuri === 'sinov' ? 'Zaxira_TuriSinov' : 'Zaxira_TuriNusxa')}`;
    if (t.oxirgiMuvaffaqiyat === false) {
      return { tur: 'xato', matn: this.til.t('Zaxira_YuborishXato', v, t.oxirgiXabar ?? ''), kutilmoqda: t.faol && t.oxirgiTuri === 'nusxa' };
    }
    return { tur: 'ok', matn: this.til.t('Zaxira_YuborishOk', v), kutilmoqda: false };
  });

  /**
   * Yoqilgan, lekin faol emas. Token ham, chat ID ham yo'q (toza o'rnatish) — xato emas: kulrang "Telegram sozlanmagan".
   * Ikkalasidan biri bor, ikkinchisi yo'q bo'lsa — qizil xato: token yo'q bo'lsa token xatosi, aks holda chat ID xatosi.
   */
  protected readonly faolIzoh = computed<{ matn: string; neytral: boolean } | null>(() => {
    const t = this.h()?.telegram;
    if (!t || !t.yoqilgan || t.faol) return null;
    if (!t.tokenMaska && !t.chatId) {
      return { matn: this.til.t('Zaxira_TelegramSozlanmagan'), neytral: true };
    }
    return { matn: this.til.t(t.tokenMaska ? 'Zaxira_XatoChatId' : 'Zaxira_XatoToken'), neytral: false };
  });

  /** "Bugun 14:02 · fuelcontrol-….db · 4 200 KB · Qo'lda". */
  protected readonly oxirgiMatn = computed(() => {
    const n = this.h()?.nusxalar[0];
    if (!n) return this.til.t('Zaxira_NusxaYoq');
    return [this.vaqtMatni(n.vaqt), n.nomi, this.hajm(n.hajmBayt), this.sabab(n)].filter((x) => x && x !== '—').join(' · ');
  });

  protected vaqtMatni(v: string): string {
    return `${kun(v) === kun(new Date()) ? this.til.t('Bugun') : kun(v)} ${soat(v)}`;
  }

  protected hajm(bayt: number): string {
    if (bayt < 1024 * 1024) return this.til.t('Zaxira_Kb', pul(bayt > 0 ? Math.max(1, Math.round(bayt / 1024)) : 0));
    return this.til.t('Zaxira_Mb', (bayt / (1024 * 1024)).toFixed(1));
  }

  protected sabab(n: ZaxiraNusxasiDto): string {
    switch (n.sabab) {
      case 'kunlik': return this.til.t('Zaxira_SababKunlik');
      case 'smena': return n.smenaId != null ? this.til.t('Zaxira_SababSmena', n.smenaId) : '—';
      case 'qolda': return this.til.t('Zaxira_SababQolda');
      default: return '—';
    }
  }

  async yukla(jim = false) {
    try {
      const z = await this.server.zaxiraHolati();
      this.h.set(z);
      this.yuklashXato.set('');
      // Boshqa foydalanuvchi o'zgartirgan bo'lsa ham, yozayotgan matn yo'qolmasin: forma faqat o'zgartirilmagan bo'lsa yangilanadi.
      if (!jim || !this.ozgardi()) this.formaniTikla(z);
    } catch (e) {
      if (!jim) this.yuklashXato.set(xatoMatni(e, this.til.t('AloqaYoq'), this.til.t('Xato_Umumiy')));
    }
  }

  private formaniTikla(z: ZaxiraHolatiDto) {
    this.token.set('');
    this.tokenSurov.set(false);
    this.chatId.set(z.telegram.chatId ?? '');
    this.yoqilgan.set(z.telegram.yoqilgan);
    this.urindi.set(false);
    this.sinovUrindi.set(false);
  }

  async olish() {
    if (this.band()) return;
    this.band.set(true);
    try {
      const z = await this.server.zaxira();
      this.bildirish.korsat(this.til.t('Zaxira_NusxaOlindi', z.faylNomi));
      await this.yukla(true);
      // Telegram'ga yuborish fon rejimida bajariladi — holat bir ozdan keyin yangilanadi.
      if (z.telegramYuboriladi) {
        clearTimeout(this.kechikish);
        this.kechikish = setTimeout(() => void this.yukla(true), 3000);
      }
    } catch (e) {
      this.bildirish.xato(e, this.til.t('AloqaYoq'), this.til.t('Xato_Umumiy'));
    } finally {
      this.band.set(false);
    }
  }

  /** Faqat o'zgargan maydonlar yuboriladi (null = o'zgarmaydi, "" = o'chirish). */
  private tahrir(): { token: string | null; chatId: string | null; yoqilgan: boolean | null } {
    const t = this.h()!.telegram;
    const chat = this.chatId().trim();
    return {
      token: this.token().trim() || null,
      chatId: chat !== (t.chatId ?? '') ? chat : null,
      yoqilgan: this.yoqilgan() !== t.yoqilgan ? this.yoqilgan() : null,
    };
  }

  async saqla(): Promise<boolean> {
    if (!this.h() || this.saqlanmoqda() || !this.ozgardi()) return false;
    this.xato.set('');
    this.urindi.set(true);
    if (this.xatoToken() || this.xatoChat()) return false;
    return this.yubor(this.tahrir());
  }

  async tokenOchir() {
    if (!this.h() || this.saqlanmoqda()) return;
    this.xato.set('');
    await this.yubor({ token: '', chatId: null, yoqilgan: null });
  }

  private async yubor(d: { token: string | null; chatId: string | null; yoqilgan: boolean | null }): Promise<boolean> {
    this.saqlanmoqda.set(true);
    try {
      const z = await this.server.zaxiraTelegramSaqla(d);
      this.h.set(z);
      this.formaniTikla(z);
      this.bildirish.korsat(this.til.t('Zaxira_TelegramSaqlandi'));
      return true;
    } catch (e) {
      this.xato.set(xatoMatni(e, this.til.t('AloqaYoq'), this.til.t('Xato_Umumiy')));
      return false;
    } finally {
      this.saqlanmoqda.set(false);
    }
  }

  /** Sinov xabari saqlangan sozlamalar bilan ketadi — forma o'zgargan bo'lsa avval saqlanadi. */
  async sinov() {
    if (!this.h() || this.sinovBand() || this.saqlanmoqda()) return;
    this.xato.set('');
    this.urindi.set(true);
    this.sinovUrindi.set(true);
    if (this.xatoToken() || this.xatoChat()) return;
    if (this.ozgardi() && !(await this.yubor(this.tahrir()))) return;
    this.sinovBand.set(true);
    try {
      const z = await this.server.zaxiraSinov();
      this.h.set(z);
      const t = z.telegram;
      if (t.oxirgiTuri === 'sinov' && t.oxirgiMuvaffaqiyat === false) this.bildirish.korsat(t.oxirgiXabar ?? this.til.t('Xato_Umumiy'), true);
      else this.bildirish.korsat(this.til.t('Zaxira_SinovYuborildi'));
    } catch (e) {
      this.xato.set(xatoMatni(e, this.til.t('AloqaYoq'), this.til.t('Xato_Umumiy')));
    } finally {
      this.sinovBand.set(false);
      this.sinovUrindi.set(false);
    }
  }
}
