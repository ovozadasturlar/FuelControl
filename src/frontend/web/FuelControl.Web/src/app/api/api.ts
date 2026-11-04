import createClient, { type Middleware } from 'openapi-fetch';
import type { paths } from './schema';

/** Server xatosi (ProblemDetails) — `detail` foydalanuvchiga ko'rsatiladi. */
export class ApiXato extends Error {
  /** ProblemDetails.extensions (masalan, `kerakliAparatlar`). */
  constructor(public readonly status: number, xabar: string, public readonly qoshimcha: Record<string, unknown> = {}) { super(xabar); }
}

/** Tarmoq xatosi (internet yo'q / server javob bermadi). */
export class AloqaXato extends Error {}

/** Auth xizmati API klientiga ulanadigan kancha (aylanma bog'liqlikdan qochish uchun u o'zini shu yerda ro'yxatdan o'tkazadi). */
export interface ApiKancha {
  token: () => string | null;
  /** Har so'rovdan oldin: faollik belgilanadi, token muddati tugab borayotgan bo'lsa fonda uzaytiriladi (kutilmaydi). */
  oldin: () => void;
  /**
   * 401 keldi (docs §8.18): kirish sahifasiga otmaymiz — joriy ekran ustida parol/PIN so'raladi. `ishlatilgan` — rad etilgan so'rovdagi token.
   * Qayta kirilsa yangi token, to'liq chiqilsa `null`. Bir vaqtda ko'p so'rov 401 olsa — hammasi bitta javobni kutadi.
   */
  tugadi: (ishlatilgan: string | null) => Promise<string | null>;
}

let kancha: ApiKancha = { token: () => null, oldin: () => {}, tugadi: async () => null };

export function apiSozla(k: ApiKancha) {
  kancha = k;
}

/** Kirish va uzaytirish so'rovlariga token/401 qoidalari qo'llanmaydi. */
const kirishYoli = (url: string) => /\/auth\/(login|yangila)$/.test(new URL(url).pathname);

/** Rad etilgan so'rovni qayta yuborish uchun asl nusxa (tanasi fetch tomonidan o'qib bo'linadi, shuning uchun oldindan clone). */
const nusxalar = new WeakMap<Request, Request>();

const auth: Middleware = {
  onRequest({ request }) {
    const t = kancha.token();
    if (t) request.headers.set('Authorization', `Bearer ${t}`);
    if (!kirishYoli(request.url)) {
      kancha.oldin();
      nusxalar.set(request, request.clone());
    }
    return request;
  },
  async onResponse({ response, request }) {
    if (response.status !== 401 || kirishYoli(request.url)) return response;
    const yangi = await kancha.tugadi(request.headers.get('Authorization')?.replace(/^Bearer /, '') ?? null);
    const asl = nusxalar.get(request);
    if (!yangi || !asl) return response;
    // Parol/PIN qayta kiritildi: rad etilgan so'rov yangi token bilan BIR marta qayta yuboriladi (yana 401 bo'lsa, o'sha javob qaytadi).
    const q = asl.clone();
    q.headers.set('Authorization', `Bearer ${yangi}`);
    return fetch(q);
  },
};

const yarat = (baseUrl: string) => {
  const c = createClient<paths>({ baseUrl });
  c.use(auth);
  return c;
};

/**
 * API klienti. Standart — shu domen. Ilova boshlanishida sozlama.json'dan o'qilgan manzil bilan `apiManzilOrnat` qayta yaratadi
 * (ESM live binding: `api` ni import qilganlar har chaqiruvda joriy qiymatni ko'radi — doim `api.GET(...)` shaklida ishlating).
 */
export let api = yarat(location.origin);

/** Boshqa domendagi API (Cloudflare Pages) — `""` bo'lsa shu domen. */
export function apiManzilOrnat(asos: string) {
  api = yarat(asos || location.origin);
}

type Natija<D> = { data?: D; error?: unknown; response: Response };

/** openapi-fetch natijasini ochadi: muvaffaqiyatda ma'lumot, aks holda ApiXato/AloqaXato. */
export async function ol<D, T = D>(sorov: Promise<Natija<D>>): Promise<T> {
  let n: Natija<D>;
  try {
    n = await sorov;
  } catch {
    throw new AloqaXato('Aloqa yo\'q');
  }
  if (n.response.ok) return n.data as unknown as T;
  const e = n.error as { detail?: string; title?: string } | string | undefined;
  const xabar = typeof e === 'string' ? e : e?.detail ?? e?.title ?? `HTTP ${n.response.status}`;
  throw new ApiXato(n.response.status, xabar);
}
