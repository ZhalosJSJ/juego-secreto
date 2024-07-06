   let numeroSecreto = 0;
   let intentos = 0;
   let ListaNumerosSorteados = [];
   let NumeroMaximo = 10; 

   console.log(numeroSecreto);

   function asignarTextoElemento(elemento, texto){
   let elementoHTML = document.querySelector(elemento);
   elementoHTML.innerHTML = texto;
   return;

   }
   function verificarIntento() {
      let numeroUsuario = parseInt(document.getElementById('ValorUsuario').value);

       console.log(intentos);

       if(numeroUsuario === numeroSecreto) {
        
         asignarTextoElemento('p',`acertastes el numero en ${intentos} ${(intentos === 1) ? 'vez' : 'veces'}`); 
         document.getElementById('reiniciar').removeAttribute('disabled');
      } else {
         if (numeroUsuario > numeroSecreto) {
            asignarTextoElemento('p','el numero secreto es menor');
         } else {
            asignarTextoElemento('p','el numero secreto es mayor'); 
         }
         intentos++;
         limpiarCaja();
      }
               return;
               }

               function limpiarCaja() {
               document.querySelector('#ValorUsuario').value ='';

               }

               function generarNumeroSecretro() {
                  let numeroGenerado = Math.floor(Math.random()*NumeroMaximo)+1;

                  console.log(numeroGenerado);
                  
                  console.log(numeroSecreto);
                  
                  console.log(ListaNumerosSorteados);

                  if (ListaNumerosSorteados.length == NumeroMaximo) {
                     asignarTextoElemento('p','Ya se sortearon todos los numeros posibles');
                  } else { 

                  if (ListaNumerosSorteados.includes(numeroGenerado)) {

                     return generarNumeroSecretro();
                  } else {
                     ListaNumerosSorteados.push(numeroGenerado);
                     return numeroGenerado;
                  }
               }
            }
               
               function CondicionesIniciales(){

                  asignarTextoElemento('h1','juego del numero secreto 2.0');
                  asignarTextoElemento('p',`indica un numero del 1 al ${NumeroMaximo}`); 
                  numeroSecreto = generarNumeroSecretro();
                  intentos = 1
               }

               function reiniciarJuego() {
                  limpiarCaja();
                 CondicionesIniciales();
                 document.querySelector('#reiniciar').setAttribute('disabled', 'true');

               }

               CondicionesIniciales();
               //este es