import { Component, DestroyRef, ElementRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Auth } from '../core/auth';
import { Til } from '../core/til';
import { ApiXato, AloqaXato } from '../api/api';
import { Ikon } from './ikon';

/**
 * "Sessiya tugadi — parol/PIN ni qayta kiriting" (docs §8.18, desktop bilan bir xil). Token muddati tugab, server 401 qaytarganda kirish sahifasiga
 * otmaymiz: joriy ekran va ochiq dialoglar joyida turadi (ular `inert` qilinadi), ustida shu oyna chiqadi. Login o'zgarmaydi, faqat parol/PIN
 * (operator uchun PIN va raqamli klaviatura). Muvaffaqiyatli kirilsa rad etilgan so'rov bir marta qayta yuboriladi. Boshqa odam kirmoqchi bo'lsa — "Chiqish".
 * Yopib bo'lmaydi: parda bosilsa ham, Escape bosilsa ham (Escape ostidagi dialogga ham yetib bormaydi).
 */
@Component({
  selector: 'sessiya-tugadi',
  imports: [FormsModule, Ikon],
  template: `
    @if (auth.qaytaKirishKerak()) {
      <div class="parda sessiya-parda">
        <form class="dialog" role="alertdialog" aria-modal="true" aria-labelledby="st-sarlavha" style="max-width: 440px" data-test="sessiya-modal"
              (ngSubmit)="kir()" autocomplete="on" novalidate>
          <div class="dialog-bosh">
            <span class="ikon-doira katta sariq"><ikon nomi="lock" [olcham]="20" [qalinlik]="2" /></span>
            <div class="matnlar">
              <h2 id="st-sarlavha" data-test="sessiya-sarlavha">{{ til.t('Sessiya_Sarlavha') }}</h2>
            </div>
          </div>
          <p class="ikkilamchi kichik-matn izoh" data-test="sessiya-izoh">{{ izoh() }}</p>
          <div class="maydon">
            <label for="st-parol">{{ til.t(pin() ? 'PinKod' : 'Parol') }}</label>
            <input #parolMaydon id="st-parol" class="kiritish" name="parol" type="password" [(ngModel)]="parol" [placeholder]="til.t('PinYokiParol')"
                   autocomplete="current-password" enterkeyhint="go" [attr.inputmode]="pin() ? 'none' : null" [attr.maxlength]="pin() ? 6 : null" [class.xato]="!!xato()" />
          </div>
          @if (pin()) {
            <div class="klaviatura" data-test="sessiya-klaviatura">
              @for (r of raqamlar; track $index) {
                @if (r === '') { <span></span> }
                @else {
                  <button type="button" class="tugma" [attr.data-raqam]="r" (click)="raqamBos(r)" [attr.aria-label]="r === '<' ? 'backspace' : r">
                    @if (r === '<') { <ikon nomi="backspace" /> } @else { {{ r }} }
                  </button>
                }
              }
            </div>
          }
          @if (xato()) { <div class="xato-matn" role="alert" data-test="sessiya-xato">{{ xato() }}</div> }
          <div class="amallar">
            <button type="button" class="tugma" data-test="sessiya-chiqish" (click)="auth.chiqish()">{{ til.t('Chiqish') }}</button>
            <button type="submit" class="tugma asosiy" data-test="sessiya-kirish" [disabled]="band()">
              @if (band()) { <span class="aylanma"></span> } @else { <ikon nomi="arrow" [olcham]="16" [qalinlik]="2.2" /> } {{ til.t('Kirish') }}
            </button>
          </div>
        </form>
      </div>
    }
  `,
  styles: `
    :host { display: contents; }
    .sessiya-parda { z-index: 1000; }
    .izoh { line-height: 1.45; }
    .klaviatura { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; }
    .klaviatura .tugma { min-height: 48px; font-size: 18px; font-weight: 700; }
  `,
})
export class SessiyaTugadi {
  protected readonly auth = inject(Auth);
  protected readonly til = inject(Til);
  protected readonly parol = signal('');
  protected readonly xato = signal('');
  protected readonly band = signal(false);
  protected readonly raqamlar = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '', '0', '<'];
  /** Operator PIN bilan kiradi (4–6 raqam); qolganlar parol bilan. */
  protected readonly pin = computed(() => this.auth.foydalanuvchi()?.rol === 'Operator');
  protected readonly izoh = computed(() => {
    const f = this.auth.foydalanuvchi();
    return f ? this.til.t(this.pin() ? 'Sessiya_IzohPin' : 'Sessiya_IzohParol', f.toliqIsm, f.login) : '';
  });
  private readonly maydon = viewChild<ElementRef<HTMLInputElement>>('parolMaydon');
  private readonly el = inject<ElementRef<HTMLElement>>(ElementRef);
  private oldingiFokus: HTMLElement | null = null;

  constructor() {
    // Escape ostidagi dialogni yopib yubormasin (ma'lumot yo'qolmasin): capture bosqichida to'xtatiladi.
    const esc = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && this.auth.qaytaKirishKerak()) { e.preventDefault(); e.stopImmediatePropagation(); }
    };
    addEventListener('keydown', esc, true);
    inject(DestroyRef).onDestroy(() => removeEventListener('keydown', esc, true));

    effect(() => {
      const och = this.auth.qaytaKirishKerak();
      untracked(() => {
        if (och) {
          this.oldingiFokus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
          this.parol.set('');
          this.xato.set('');
          this.ostini(true);
          setTimeout(() => this.maydon()?.nativeElement.focus(), 30);
        } else {
          this.ostini(false);
          const f = this.oldingiFokus;
          this.oldingiFokus = null;
          // Dialogdagi maydon fokusini qaytaramiz (kirishdan keyin yozishni davom ettirish uchun).
          if (f?.isConnected) setTimeout(() => f.focus(), 0);
        }
      });
    });
  }

  /** Oyna ochiq paytda ostidagi hamma narsa (sahifa, dialoglar) bosilmaydi va fokus olmaydi, lekin holati saqlanadi. */
  private ostini(inert: boolean) {
    const ota = this.el.nativeElement.parentElement;
    if (!ota) return;
    for (const b of Array.from(ota.children)) {
      if (b === this.el.nativeElement || b.classList.contains('bildirish')) continue;
      (b as HTMLElement).inert = inert;
    }
  }

  protected raqamBos(r: string) {
    if (r === '<') this.parol.update((p) => p.slice(0, -1));
    else if (this.parol().length < 6) this.parol.update((p) => p + r);
  }

  protected async kir() {
    if (this.band()) return;
    this.xato.set('');
    const p = this.parol();
    if (this.pin() ? p.length < 4 : p.length === 0) return this.xato.set(this.til.t(this.pin() ? 'Xato_Pin' : 'ParolniKiriting'));
    this.band.set(true);
    try {
      await this.auth.qaytaKirish(p);
    } catch (e) {
      this.parol.set('');
      if (e instanceof ApiXato && e.status === 403) { this.auth.chiqish(true); return; } // bloklangan/nofaol — avvalgidek to'liq chiqiladi
      this.xato.set(
        e instanceof AloqaXato ? this.til.t('AloqaYoq')
        : e instanceof ApiXato && e.status === 401 ? this.til.t('Xato_Login')
        : e instanceof ApiXato ? e.message // 429: server matni (qancha kutish kerakligi)
        : this.til.t('Xato_Umumiy'));
      setTimeout(() => this.maydon()?.nativeElement.focus(), 0);
    } finally {
      this.band.set(false);
    }
  }
}
