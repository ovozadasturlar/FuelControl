import { Component, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { SwUpdate } from '@angular/service-worker';
import { filter } from 'rxjs';
import { Bildirish } from './core/bildirish';
import { Til } from './core/til';
import { Tema } from './core/tema';
import { Ornatish } from './core/ornatish';
import { Ikon } from './ui/ikon';
import { SessiyaTugadi } from './ui/sessiya-tugadi';

const AVTO_KALIT = 'fc.avtoYangilandi';
/** Ilova ochilgandan shuncha vaqt ichida yangi versiya topilsa — darhol (foydalanuvchi hali hech narsa kiritmagan) almashtiriladi. */
const BOSHLANISH_ONGI_MS = 20000;
/** Yangilanishni qayta-qayta tekshirish: ilova ko'ringanda va shu oraliqda (ochiq turgan PWA ham eskirib qolmasin). */
const TEKSHIRISH_ORALIGI_MS = 10 * 60 * 1000;

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Ikon, SessiyaTugadi],
  template: `
    <router-outlet />
    <sessiya-tugadi />
    @if (bildirish.joriy(); as x) {
      <div class="bildirish shisha" [class.xato]="x.xato" role="status" aria-live="polite">
        <ikon [nomi]="x.xato ? 'x' : 'check'" [olcham]="16" [qalinlik]="2.2" /> {{ x.matn }}
      </div>
    }
    @if (yangiVersiya()) {
      <div class="bildirish shisha" role="alert" style="bottom:auto;top:calc(12px + env(safe-area-inset-top))">
        <ikon nomi="refresh" [olcham]="16" [qalinlik]="2.2" /> {{ til.t('YangiVersiya') }}
        <button class="tugma kichik asosiy" (click)="qaytaYukla()">{{ til.t('Yangilash2') }}</button>
      </div>
    }
  `,
})
export class App {
  protected readonly bildirish = inject(Bildirish);
  protected readonly til = inject(Til);
  protected readonly yangiVersiya = signal(false);
  private readonly boshlandi = Date.now();

  constructor() {
    inject(Tema);
    inject(Ornatish); // beforeinstallprompt sahifa yuklanishi bilan keladi — erta ushlaymiz
    const sw = inject(SwUpdate);
    if (!sw.isEnabled) return;

    // Yangi versiya tayyor: ilova endi ochilgan bo'lsa darhol, aks holda xabar + keyingi sahifa almashishida (docs §8.5).
    sw.versionUpdates.pipe(filter((e) => e.type === 'VERSION_READY')).subscribe(() => this.yangilandi());
    // Keshdagi nusxa buzilgan (masalan, eski hash'li fayl serverdan olib tashlangan) — qayta yuklaymiz.
    sw.unrecoverable.subscribe(() => location.reload());

    const tekshir = () => { sw.checkForUpdate().catch(() => undefined); };
    tekshir();
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') tekshir(); });
    setInterval(tekshir, TEKSHIRISH_ORALIGI_MS);

    // Yangi versiya kutib turgan bo'lsa, foydalanuvchi boshqa sahifaga o'tganda yangilaymiz (yarim to'ldirilgan forma yo'qolmasin).
    inject(Router).events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => { if (this.yangiVersiya()) this.qaytaYukla(); });
  }

  private yangilandi() {
    const yaqinda = (() => { try { return Date.now() - Number(sessionStorage.getItem(AVTO_KALIT) ?? 0) < 60000; } catch { return true; } })();
    if (Date.now() - this.boshlandi < BOSHLANISH_ONGI_MS && !yaqinda) {
      try { sessionStorage.setItem(AVTO_KALIT, String(Date.now())); } catch { /* */ }
      this.qaytaYukla(); // bir marta; qayta-yuklash sikli bo'lmasin
      return;
    }
    this.yangiVersiya.set(true);
  }

  qaytaYukla() {
    location.reload();
  }
}
