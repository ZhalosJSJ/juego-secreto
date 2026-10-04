"""Pruebas de la geometría de los widgets (corren en cualquier sistema).

    python -m unittest discover -s pruebas
"""
import unittest

from ventanas import MARGEN, Pantalla, Rect, dentro, elegir_pantalla, rect_inicial, ubicar


def pantalla(nombre, x, y, ancho, alto, principal=False, barra=48):
    return Pantalla(nombre, Rect(x, y, ancho, alto), Rect(x, y, ancho, alto - barra), principal)


PRINCIPAL = pantalla(r"\\.\DISPLAY1", 0, 0, 1920, 1080, principal=True)
DERECHA = pantalla(r"\\.\DISPLAY2", 1920, 0, 2560, 1440)
IZQUIERDA = pantalla(r"\\.\DISPLAY3", -1280, 0, 1280, 1024)


class ElegirPantalla(unittest.TestCase):
    def test_secundaria_por_defecto(self):
        self.assertEqual(elegir_pantalla([PRINCIPAL, DERECHA]), DERECHA)

    def test_una_sola_pantalla_usa_la_principal(self):
        self.assertEqual(elegir_pantalla([PRINCIPAL]), PRINCIPAL)

    def test_secundaria_a_la_izquierda(self):
        self.assertEqual(elegir_pantalla([PRINCIPAL, IZQUIERDA]), IZQUIERDA)

    def test_principal(self):
        self.assertEqual(elegir_pantalla([DERECHA, PRINCIPAL], "principal"), PRINCIPAL)

    def test_numero_cuenta_de_izquierda_a_derecha(self):
        todas = [PRINCIPAL, DERECHA, IZQUIERDA]
        self.assertEqual(elegir_pantalla(todas, 1), IZQUIERDA)
        self.assertEqual(elegir_pantalla(todas, "2"), PRINCIPAL)
        self.assertEqual(elegir_pantalla(todas, 3), DERECHA)

    def test_numero_inexistente_usa_la_principal(self):
        self.assertEqual(elegir_pantalla([PRINCIPAL, DERECHA], 5), PRINCIPAL)

    def test_sin_pantallas(self):
        with self.assertRaises(ValueError):
            elegir_pantalla([])


class Posiciones(unittest.TestCase):
    trabajo = DERECHA.trabajo

    def test_derecha_a_todo_lo_alto(self):
        r = rect_inicial(self.trabajo, 480, 0, "derecha")
        self.assertEqual(r.ancho, 480)
        self.assertEqual(r.alto, self.trabajo.alto - 2 * MARGEN)
        self.assertEqual(r.x + r.ancho, self.trabajo.x + self.trabajo.ancho - MARGEN)
        self.assertEqual(r.y, self.trabajo.y + MARGEN)

    def test_arriba_izquierda(self):
        r = rect_inicial(self.trabajo, 280, 120, "arriba-izquierda")
        self.assertEqual((r.x, r.y, r.ancho, r.alto), (self.trabajo.x + MARGEN, MARGEN, 280, 120))

    def test_lado_desconocido_va_a_la_derecha(self):
        self.assertEqual(rect_inicial(self.trabajo, 300, 200, "???"), rect_inicial(self.trabajo, 300, 200, "derecha"))

    def test_mas_grande_que_la_pantalla_se_achica(self):
        r = rect_inicial(self.trabajo, 9000, 9000, "centro")
        self.assertEqual(r.ancho, self.trabajo.ancho - 2 * MARGEN)
        self.assertEqual(r.alto, self.trabajo.alto - 2 * MARGEN)

    def test_dentro_corrige_lo_que_se_sale(self):
        r = dentro(Rect(-500, 5000, 300, 200), self.trabajo)
        self.assertEqual((r.x, r.y), (self.trabajo.x, self.trabajo.y + self.trabajo.alto - 200))

    def test_posicion_guardada_es_relativa_a_la_pantalla(self):
        r = ubicar(self.trabajo, {"ancho": 300, "alto": 200}, {"dx": 100, "dy": 50})
        self.assertEqual((r.x, r.y), (self.trabajo.x + 100, self.trabajo.y + 50))

    def test_posicion_guardada_en_pantalla_mas_chica_queda_dentro(self):
        chica = PRINCIPAL.trabajo
        r = ubicar(chica, {"ancho": 300, "alto": 200}, {"dx": 2400, "dy": 1300})
        self.assertEqual(r, dentro(r, chica))
        self.assertEqual(r.x + r.ancho, chica.x + chica.ancho)


if __name__ == "__main__":
    unittest.main()
