import { Injectable, signal, computed, inject, effect, untracked } from '@angular/core';
import type { HubConnection } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { Auth } from './auth';
import { Sozlama } from './sozlama';

/** SignalR hodisasi — faqat "qayta yuklash" signali (yuk e'tiborsiz: smena uchun GET /smenalar/joriy). */
export type HodisaTuri = 'SmenaOzgardi' | 'NasiyaOzgardi' | 'XarajatOzgardi' | 'AparatOzgardi' | 'NarxOzgardi';
export interface HubHodisa { turi: HodisaTuri }

/**
 * Tarmoq holati va SignalR (/hub). Kirgan foydalanuvchi uchun ulanadi, uzilsa cheksiz qayta urinadi.
 * Sahifalar `hodisa$` ga obuna bo'lib ma'lumotni yangilaydi.
 */
@Injectable({ providedIn: 'root' })
export class Aloqa {
  private readonly auth = inject(Auth);
  private readonly sozlama = inject(Sozlama);
  readonly onlayn = signal(navigator.onLine);
  readonly hubHolati = signal<'uzilgan' | 'ulanmoqda' | 'ulangan'>('uzilgan');
  /** Banner: internet yo'q yoki hub qayta ulanmoqda. */
  readonly muammo = computed<'yoq' | 'aloqaYoq' | 'qaytaUlanmoqda'>(() =>
    !this.onlayn() ? 'aloqaYoq' : this.auth.kirganmi() && this.hubHolati() !== 'ulangan' && this.oldinUlangan() ? 'qaytaUlanmoqda' : 'yoq');
  private readonly oldinUlangan = signal(false);
  readonly hodisa$ = new Subject<HubHodisa>();
  /** Qayta ulanganda (yoki internet qaytganda) — sahifalar to'liq yangilanadi, navbat yuboriladi. */
  readonly tiklandi$ = new Subject<void>();

  private hub: HubConnection | null = null;
  private qaytaTaymer: ReturnType<typeof setTimeout> | undefined;
  /** "QaytaUlan" dan keyin onclose kelganda kutmasdan ulanish uchun. */
  private qaytaUlanKutilmoqda = false;

  constructor() {
    addEventListener('online', () => { this.onlayn.set(true); this.tiklandi$.next(); this.boshla(); });
    addEventListener('offline', () => this.onlayn.set(false));
    effect(() => (this.auth.kirganmi() ? this.boshla() : this.toxta()));
    // Token almashganda (fon uzaytirish yoki "Sessiya tugadi" oynasidan qayta kirish): hub uzilgan bo'lsa 10 soniya kutmasdan yangi token bilan ulanamiz.
    effect(() => {
      const token = this.auth.sessiya()?.token;
      untracked(() => { if (token && this.hub && this.hubHolati() === 'uzilgan') this.boshla(); });
    });
  }

  private async boshla() {
    if (!this.auth.kirganmi()) return;
    // SignalR alohida chunk'da — login sahifasi va birinchi ochilish yengilroq.
    const { HubConnectionBuilder, HubConnectionState, LogLevel } = await import('@microsoft/signalr');
    if (!this.auth.kirganmi()) return;
    if (!this.hub) {
      const hub = new HubConnectionBuilder()
        // Token header/query orqali ketadi (cookie yo'q) — cross-origin'da withCredentials shart emas.
        .withUrl(this.sozlama.yol('/hub'), { accessTokenFactory: () => this.auth.token() ?? '', withCredentials: false })
        .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (c) => Math.min(30000, 1000 * 2 ** Math.min(c.previousRetryCount, 5)) })
        .configureLogging(LogLevel.Warning)
        .build();
      for (const turi of ['SmenaOzgardi', 'NasiyaOzgardi', 'XarajatOzgardi', 'AparatOzgardi', 'NarxOzgardi'] as const) {
        hub.on(turi, () => this.hodisa$.next({ turi }));
      }
      // Server foydalanuvchi ruxsati/roli o'zgarganda ulanishni uzadi: /me ni yangilab darhol qayta ulanamiz.
      hub.on('QaytaUlan', async () => {
        this.qaytaUlanKutilmoqda = true;
        await this.auth.yangila();
        this.boshla();
      });
      hub.onreconnecting(() => this.hubHolati.set('ulanmoqda'));
      hub.onreconnected(() => { this.qaytaUlanKutilmoqda = false; this.hubHolati.set('ulangan'); this.tiklandi$.next(); });
      // Avtomatik qayta ulanish ham to'xtasa — o'zimiz qayta boshlaymiz.
      hub.onclose(() => {
        this.hubHolati.set('uzilgan');
        if (this.qaytaUlanKutilmoqda) { this.qaytaUlanKutilmoqda = false; this.boshla(); return; }
        this.keyinroq();
      });
      this.hub = hub;
    }
    if (this.hub.state !== HubConnectionState.Disconnected) return;
    try {
      this.hubHolati.set('ulanmoqda');
      await this.hub.start();
      this.hubHolati.set('ulangan');
      if (this.oldinUlangan()) this.tiklandi$.next();
      this.oldinUlangan.set(true);
    } catch {
      this.hubHolati.set('uzilgan');
      this.keyinroq();
    }
  }

  private keyinroq() {
    clearTimeout(this.qaytaTaymer);
    if (this.auth.kirganmi()) this.qaytaTaymer = setTimeout(() => this.boshla(), 10000);
  }

  private toxta() {
    clearTimeout(this.qaytaTaymer);
    const h = this.hub;
    this.hub = null;
    this.hubHolati.set('uzilgan');
    this.oldinUlangan.set(false);
    h?.stop();
  }
}
