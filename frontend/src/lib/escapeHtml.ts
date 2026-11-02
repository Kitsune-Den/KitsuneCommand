// For text going into HTML strings, such as Leaflet popups and tooltips
export function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/g, c => `&#${c.charCodeAt(0)};`)
}
