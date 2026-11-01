import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Auth } from '../../core/auth';
import { BUILD_BELGISI } from '../../core/versiya';
import { Til, TilKodi } from '../../core/til';
import { Tema, TemaRejimi } from '../../core/tema';
import { Bildirish } from '../../core/bildirish';
import { jonliYangila } from '../../core/malumot';
import { Ikon } from '../../ui/ikon';
import { OrnatishTaklif } from '../../ui/ornatish-taklif';
import { SozlamalarXizmati } from './sozlamalar-xizmati';
import { NarxlarBolimi } from './narxlar';
import { AparatlarBolimi } from './aparatlar';
import { FoydalanuvchilarBolimi } from './foydalanuvchilar';
import { RuxsatlarBolimi } from './ruxsatlar';
import { ZaxiraBolimi } from './zaxira';

const BOLIM_KALIT = 'fc.sozlamalarBolimi';

/**
 * Sozlamalar. "Sozlamalar" ruxsati borlarga — 5 bo'lim (docs/dizayn/Sozlamalar*): yoqilg'i narxlari, aparatlar, foydalanuvchilar,
 * ruxsatlar, zaxira nusxa. Boshqalarga (telefonda "Yana" varag'idan) — faqat til, tema, o'rnatish va chiqish.
 */
@Component({
  selector: 'sozlamalar-sahifa',
  imports: [Ikon, OrnatishTaklif, NarxlarBolimi, AparatlarBolimi, FoydalanuvchilarBolimi, RuxsatlarBolimi, ZaxiraBolimi],
  providers: [SozlamalarXizmati],
  template: `
    @if (admin()) {
      <div class="sahifa">
        <header class="sahifa-bosh sarlavha-ustun">
          <h1>{{ til.t('Sozlamalar') }}</h1>
          <nav class="bolimlar" [attr.aria-label]="til.t('Sozlama_Bolimlari')">
            @for (b of bolimlar; track b.kalit; let i = $index) {
              <button type="button" class="bolim-chip" [class.tanlangan]="bolim() === i" [attr.aria-current]="bolim() === i ? 'page' : null" (click)="tanla(i)">{{ til.t(b.kalit) }}</button>
            }
          </nav>
        </header>

        @if (!x.yuklandi()) {
          <div class="shisha karta"><div class="skelet" style="height:180px"></div></div>
        } @else if (x.xato()) {
          <div class="shisha karta bosh">
            {{ x.xato() }}<br /><br />
            <button type="button" class="tugma" (click)="yukla()"><ikon nomi="refresh" [olcham]="16" /> {{ til.t('Yangilash') }}</button>
          </div>
        } @else {
          @switch (bolim()) {
            @case (0) { <sozlamalar-narxlar /> }
            @case (1) { <sozlamalar-aparatlar /> }
            @case (2) { <sozlamalar-foydalanuvchilar /> }
            @case (3) { <sozlamalar-ruxsatlar /> }
            @case (4) { <sozlamalar-zaxira /> }
          }
        }

        <!-- Brauzer o'rnatishni taklif qilsa (yoki iOS'da) — sahifa pastida -->
        <ornatish-taklif />
      </div>
    } @else {
      <!-- "Sozlamalar" ruxsati yo'q foydalanuvchi: faqat til, tema, o'rnatish va chiqish -->
      <div class="sahifa oddiy">
        <header class="sahifa-bosh"><div class="sarlavha"><h1>{{ til.t('Sozlamalar') }}</h1></div></header>

        <section class="shisha karta">
          <div class="qator">
            <span class="ikon-doira"><ikon nomi="user" [olcham]="18" /></span>
            <div class="bosh-joy">
              <div class="qalin">{{ auth.foydalanuvchi()?.toliqIsm }}</div>
              <div class="ikkilamchi kichik-matn">{{ auth.foydalanuvchi()?.login }} · {{ til.t('Rol_' + auth.foydalanuvchi()?.rol) }}</div>
            </div>
          </div>
        </section>

        <section class="shisha karta">
          <h2><ikon nomi="globe" [olcham]="18" /> {{ til.t('Til') }}</h2>
          <div class="segment">
            @for (t of tillar; track t.kod) {
              <button type="button" [class.tanlangan]="til.kod() === t.kod" [attr.aria-pressed]="til.kod() === t.kod" (click)="til.tanla(t.kod)">{{ til.t(t.kalit) }}</button>
            }
          </div>
          <h2><ikon [nomi]="tema.qorongimi() ? 'moon' : 'sun'" [olcham]="18" /> {{ til.t('RejimIzoh') }}</h2>
          <div class="segment">
            @for (r of rejimlar; track r.kod) {
              <button type="button" [class.tanlangan]="tema.rejim() === r.kod" [attr.aria-pressed]="tema.rejim() === r.kod" (click)="tema.rejim.set(r.kod)">{{ til.t(r.kalit) }}</button>
            }
          </div>
        </section>

        <ornatish-taklif />

        <button type="button" class="tugma xavfli keng" (click)="auth.chiqish()"><ikon nomi="power" [olcham]="18" /> {{ til.t('Chiqish') }}</button>
        <p class="ikkilamchi kichik-matn versiya">FuelControl PWA · {{ til.t('Versiya') }} {{ versiya }}</p>
      </div>
    }
  `,
  styles: `
    :host { display: block; }
    .sarlavha-ustun { flex-direction: column; align-items: stretch; gap: 14px; }
    .bolimlar { display: flex; flex-wrap: wrap; gap: 8px; }
    .bolim-chip {
      display: inline-flex; align-items: center; min-height: 44px; padding: 0 16px; border-radius: 999px; cursor: pointer;
      font-size: 13.5px; font-weight: 600; color: var(--tugma-matn); background: var(--tugma-fon); border: 1px solid var(--tugma-chegara);
    }
    .bolim-chip.tanlangan {
      color: #fff; border-color: rgba(255, 255, 255, 0.55);
      background: linear-gradient(180deg, #6A98FF 0%, #2F6BFF 55%, #1F55E6 100%); box-shadow: 0 6px 14px rgba(47, 107, 255, 0.28);
    }
    .oddiy { max-width: 720px; }
    .oddiy h2 { display: flex; align-items: center; gap: 8px; margin-top: 4px; }
    .versiya { text-align: center; }
  `,
})
export class SozlamalarSahifa {
  protected readonly auth = inject(Auth);
  protected readonly versiya = BUILD_BELGISI;
  protected readonly til = inject(Til);
  protected readonly tema = inject(Tema);
  protected readonly x = inject(SozlamalarXizmati);
  private readonly bildirish = inject(Bildirish);

  protected readonly admin = computed(() => this.auth.bor('Sozlamalar'));

  /** `?qism=aparatlar|yoqilgi|zaxira` — shu bo'lim ochiladi (smena ochish uchun aparat yo'q bo'lganda Savdo/Boshqaruv'dan). */
  readonly qism = input<string | undefined>();

  protected readonly tillar: { kod: TilKodi; kalit: string }[] = [{ kod: 'uz', kalit: 'Lotin' }, { kod: 'uzk', kalit: 'Kirill' }, { kod: 'ru', kalit: 'Rus' }];
  protected readonly rejimlar: { kod: TemaRejimi; kalit: string }[] = [
    { kod: 'yorug', kalit: 'Yorug' }, { kod: 'qorongi', kalit: 'Qorongi' }, { kod: 'tizim', kalit: 'Tizim' },
  ];
  protected readonly bolimlar = [
    { kalit: 'YoqilgiNarxlari' }, { kalit: 'Aparatlar' }, { kalit: 'Foydalanuvchilar' }, { kalit: 'Ruxsatlar' }, { kalit: 'ZaxiraNusxa' },
  ];
  protected readonly bolim = signal(this.saqlangan());

  constructor() {
    effect(() => {
      const q = this.qism();
      if (q === 'aparatlar') untracked(() => this.tanla(1));
      else if (q === 'yoqilgi') untracked(() => this.tanla(0));
      else if (q === 'zaxira') untracked(() => this.tanla(4));
    });
    effect(() => {
      const i = this.x.otish();
      if (i != null) untracked(() => { this.tanla(i); this.x.otish.set(null); });
    });
    // Ruxsat berilganda (yoki sahifa ochilganda) ma'lumotlar bir marta yuklanadi.
    effect(() => { if (this.admin()) untracked(() => this.yukla()); });
    // Narx/aparat/smena boshqa qurilmadan o'zgarsa yoki aloqa tiklansa — ro'yxatlar yangilanadi.
    jonliYangila(() => { if (this.admin()) this.yukla(true); }, (h) => h.turi === 'NarxOzgardi' || h.turi === 'AparatOzgardi' || h.turi === 'SmenaOzgardi');
  }

  async yukla(jim = false) {
    try {
      await this.x.yukla();
    } catch (e) {
      if (!jim) this.bildirish.xato(e, this.til.t('AloqaYoq'), this.til.t('Xato_Umumiy'));
    }
  }

  protected tanla(i: number) {
    this.bolim.set(i);
    try { sessionStorage.setItem(BOLIM_KALIT, String(i)); } catch { /* */ }
  }

  private saqlangan(): number {
    try {
      const i = Number(sessionStorage.getItem(BOLIM_KALIT));
      return Number.isInteger(i) && i >= 0 && i < 5 ? i : 0;
    } catch {
      return 0;
    }
  }
}
