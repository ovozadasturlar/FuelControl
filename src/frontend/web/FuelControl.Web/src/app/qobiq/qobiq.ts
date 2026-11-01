import { Component, ElementRef, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { Auth } from '../core/auth';
import { Til, TilKodi } from '../core/til';
import { Tema } from '../core/tema';
import { Aloqa } from '../core/aloqa';
import { Server } from '../core/server';
import { korinadiganlar } from '../core/bolimlar';
import { jonliYangila } from '../core/malumot';
import { orqagaBogla } from '../core/orqaga';
import { BUILD_BELGISI } from '../core/versiya';
import { Ikon } from '../ui/ikon';

/**
 * Asosiy qobiq: keng ekranda yon menyu (docs/dizayn/Menyu), telefonda pastki tab-bar (ruxsatga qarab) va "Yana" varag'i
 * (qolgan bo'limlar, til, tema, chiqish); yuqorida aloqa banneri.
 */
@Component({
  selector: 'qobiq',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Ikon],
  templateUrl: './qobiq.html',
  styleUrl: './qobiq.scss',
})
export class Qobiq {
  protected readonly auth = inject(Auth);
  protected readonly til = inject(Til);
  protected readonly tema = inject(Tema);
  protected readonly aloqa = inject(Aloqa);
  private readonly server = inject(Server);
  private readonly el = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly tillar: { kod: TilKodi; nom: string }[] = [{ kod: 'uz', nom: 'UZ' }, { kod: 'uzk', nom: 'ЎЗ' }, { kod: 'ru', nom: 'RU' }];
  /** Yon menyu: Sozlamalar faqat ruxsat bilan; telefonda "Yana" varag'ida har kimga. */
  protected readonly bolimlar = computed(() => korinadiganlar(this.auth));
  /** Tab-bar: 4 tagacha bo'lim + "Yana". */
  protected readonly tablar = computed(() => this.bolimlar().filter((b) => b.yol !== 'sozlamalar').slice(0, 4));
  protected readonly qolganlar = computed(() => this.bolimlar().filter((b) => b.yol !== 'sozlamalar').slice(4));
  protected readonly versiya = BUILD_BELGISI;
  protected readonly hisobimBor = computed(() => !this.auth.bor('Operatorlar') && this.auth.bor('Savdo'));
  protected readonly yanaOchiq = signal(false);
  private readonly yanaOrqaga = orqagaBogla(this.yanaOchiq, false);
  protected readonly yanaFaol = signal(false);
  protected readonly muddatiOtgan = signal(0);
  protected readonly ulangan = computed(() => this.aloqa.onlayn() && this.aloqa.hubHolati() === 'ulangan');
  protected readonly bosh = computed(() => {
    const f = this.auth.foydalanuvchi();
    return f ? f.toliqIsm.split(/\s+/).map((s) => s[0]).slice(0, 2).join('').toUpperCase() : '';
  });

  constructor() {
    const router = inject(Router);
    router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe((e) => {
      // Varaqdagi havola bosildi — yangi sahifa tarixda, orqaga qaytarmaymiz.
      if (this.yanaOchiq()) { this.yanaOrqaga.unut(); this.yanaOchiq.set(false); }
      const yol = (e as NavigationEnd).urlAfterRedirects.split('/')[1]?.split('?')[0] ?? '';
      this.yanaFaol.set(yol === 'sozlamalar' || yol === 'hisobim' || this.qolganlar().some((b) => b.yol === yol));
      setTimeout(() => this.faolBandniKorsat(), 80);
    });
    // Ruxsatlar desktop'da o'zgargan bo'lishi mumkin.
    this.auth.yangila();
    this.belgiYangila();
    jonliYangila(() => this.belgiYangila(), (h) => h.turi === 'NasiyaOzgardi');
  }

  /** Past oynada menyu ro'yxati skroll qiladi: faol band ko'rinish oynasidan tashqarida bo'lsa, ro'yxat unga suriladi (sahifa skrolli o'zgarmaydi). */
  private faolBandniKorsat() {
    const m = this.el.nativeElement.querySelector<HTMLElement>('.yon .menyu');
    const a = m?.querySelector<HTMLElement>('.nav.faol');
    if (!m || !a || m.scrollHeight <= m.clientHeight) return;
    const mr = m.getBoundingClientRect(), ar = a.getBoundingClientRect();
    if (ar.bottom > mr.bottom - 14) m.scrollTop += ar.bottom - (mr.bottom - 14);
    else if (ar.top < mr.top + 4) m.scrollTop -= mr.top + 4 - ar.top;
  }

  /** Menyudagi "Nasiyalar" belgisi — muddati o'tgan qarzlar soni. */
  private async belgiYangila() {
    if (!this.auth.bor('Nasiyalar')) { this.muddatiOtgan.set(0); return; }
    try { this.muddatiOtgan.set((await this.server.nasiyalar('faol')).xulosa.muddatiOtganSoni); } catch { /* belgi muhim emas */ }
  }
}
