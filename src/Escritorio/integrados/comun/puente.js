// Puente entre la página de un widget y Escritorio, y el tema.
// Uso: <script src="../comun/puente.js"></script> (en la cabecera, para que el tema se aplique antes de pintar).
//   Escritorio.pedir('baterias')              → le pide datos a la app
//   Escritorio.al('baterias', d => …)         → recibe cada mensaje con ese «tipo»
//   Escritorio.enviar({ accion: 'siguiente' }) → cualquier mensaje
//   Escritorio.opcion('formato', '24')        → una «opción» del widget (?formato=12 en la dirección)
window.Escritorio = (() => {
  const puente = window.chrome && window.chrome.webview;
  const oyentes = {};
  const parametros = new URLSearchParams(location.search);
  const opcion = (nombre, porDefecto) => parametros.get(nombre) ?? porDefecto;

  const tema = opcion('tema', 'oscuro');
  document.documentElement.dataset.tema = ['claro', 'auto'].includes(tema) ? tema : 'oscuro';

  if (puente) {
    puente.addEventListener('message', e => {
      const d = e.data;
      if (d && typeof d === 'object' && d.tipo && oyentes[d.tipo]) oyentes[d.tipo].forEach(f => f(d));
    });
  }

  return {
    conectado: !!puente,
    enviar: mensaje => { if (puente) puente.postMessage(mensaje); },
    pedir: tema => { if (puente) puente.postMessage({ pedir: tema }); },
    al: (tipo, funcion) => { (oyentes[tipo] ||= []).push(funcion); },
    opcion,
  };
})();
