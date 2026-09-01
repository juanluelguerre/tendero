/**
 * Formato de precio para las dos apps. Estaba escrito identico en la pagina de
 * busqueda del storefront y en la cola de revision del backoffice.
 *
 * Se comparte porque es COMPORTAMIENTO, no identidad (ADR 0010): agrupacion de
 * millares, posicion del simbolo y separador decimal cambian con la cultura, y
 * el Intl del navegador ya sabe hacerlo. Lo que NO se comparte es como se ve el
 * precio — eso es de cada surface, y vive en su CSS.
 */
export function formatPrice(amount: number, currency: string, culture: string): string {
  return new Intl.NumberFormat(culture, { style: 'currency', currency }).format(amount);
}
