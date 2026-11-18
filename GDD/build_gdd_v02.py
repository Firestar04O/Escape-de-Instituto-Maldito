from pathlib import Path
import re

from docx import Document
from docx.enum.section import WD_SECTION
from docx.enum.table import WD_CELL_VERTICAL_ALIGNMENT, WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Inches, Pt, RGBColor


ROOT = Path(__file__).resolve().parent
OUTPUT = ROOT / "GDD Escape de Instituto Maldito v0.2 MVP.docx"

BLACK = "000000"
DARK_BLUE = "17365D"
PALE_BLUE = "EAF2F8"
LIGHT_GRAY = "D9D9D9"
MID_GRAY = "666666"


def set_run_font(run, name="Aptos", size=10.5, bold=None, color=BLACK):
    run.font.name = name
    run._element.get_or_add_rPr().rFonts.set(qn("w:ascii"), name)
    run._element.get_or_add_rPr().rFonts.set(qn("w:hAnsi"), name)
    run.font.size = Pt(size)
    if bold is not None:
        run.bold = bold
    run.font.color.rgb = RGBColor.from_string(color)


def set_cell_shading(cell, fill):
    tc_pr = cell._tc.get_or_add_tcPr()
    shd = tc_pr.find(qn("w:shd"))
    if shd is None:
        shd = OxmlElement("w:shd")
        tc_pr.append(shd)
    shd.set(qn("w:fill"), fill)


def set_cell_margins(cell, top=100, start=120, bottom=100, end=120):
    tc = cell._tc
    tc_pr = tc.get_or_add_tcPr()
    tc_mar = tc_pr.first_child_found_in("w:tcMar")
    if tc_mar is None:
        tc_mar = OxmlElement("w:tcMar")
        tc_pr.append(tc_mar)
    for margin, value in (("top", top), ("start", start), ("bottom", bottom), ("end", end)):
        node = tc_mar.find(qn(f"w:{margin}"))
        if node is None:
            node = OxmlElement(f"w:{margin}")
            tc_mar.append(node)
        node.set(qn("w:w"), str(value))
        node.set(qn("w:type"), "dxa")


def set_table_borders(table, color=LIGHT_GRAY, size="6"):
    tbl_pr = table._tbl.tblPr
    borders = tbl_pr.find(qn("w:tblBorders"))
    if borders is None:
        borders = OxmlElement("w:tblBorders")
        tbl_pr.append(borders)
    for edge in ("top", "left", "bottom", "right", "insideH", "insideV"):
        element = borders.find(qn(f"w:{edge}"))
        if element is None:
            element = OxmlElement(f"w:{edge}")
            borders.append(element)
        element.set(qn("w:val"), "single")
        element.set(qn("w:sz"), size)
        element.set(qn("w:space"), "0")
        element.set(qn("w:color"), color)


def repeat_table_header(row):
    tr_pr = row._tr.get_or_add_trPr()
    tbl_header = OxmlElement("w:tblHeader")
    tbl_header.set(qn("w:val"), "true")
    tr_pr.append(tbl_header)


def keep_table_row_together(row):
    tr_pr = row._tr.get_or_add_trPr()
    if tr_pr.find(qn("w:cantSplit")) is None:
        tr_pr.append(OxmlElement("w:cantSplit"))


def format_cell(cell, bold=False, color=BLACK, align=WD_ALIGN_PARAGRAPH.LEFT, size=9.5):
    cell.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER
    set_cell_margins(cell)
    for paragraph in cell.paragraphs:
        paragraph.alignment = align
        paragraph.paragraph_format.space_after = Pt(0)
        paragraph.paragraph_format.line_spacing = 1.05
        for run in paragraph.runs:
            set_run_font(run, size=size, bold=bold, color=color)


def add_table(doc, headers, rows, widths=None, center_cols=None):
    table = doc.add_table(rows=1, cols=len(headers))
    table.alignment = WD_TABLE_ALIGNMENT.CENTER
    table.autofit = False
    header = table.rows[0]
    repeat_table_header(header)
    keep_table_row_together(header)
    for index, text in enumerate(headers):
        header.cells[index].text = text
        set_cell_shading(header.cells[index], DARK_BLUE)
        if widths:
            header.cells[index].width = Inches(widths[index])
        format_cell(header.cells[index], bold=True, color="FFFFFF", align=WD_ALIGN_PARAGRAPH.CENTER)
    center_cols = set(center_cols or [])
    for row_index, values in enumerate(rows):
        new_row = table.add_row()
        keep_table_row_together(new_row)
        cells = new_row.cells
        for index, value in enumerate(values):
            cells[index].text = str(value)
            if widths:
                cells[index].width = Inches(widths[index])
            if row_index % 2 == 1:
                set_cell_shading(cells[index], PALE_BLUE)
            align = WD_ALIGN_PARAGRAPH.CENTER if index in center_cols else WD_ALIGN_PARAGRAPH.LEFT
            format_cell(cells[index], align=align)
    set_table_borders(table)
    doc.add_paragraph().paragraph_format.space_after = Pt(0)
    return table


def add_heading(doc, text, level=1):
    paragraph = doc.add_heading(text, level=level)
    paragraph.paragraph_format.keep_with_next = True
    return paragraph


def add_body(doc, text, bold_lead=None):
    paragraph = doc.add_paragraph()
    paragraph.paragraph_format.widow_control = True
    if bold_lead and text.startswith(bold_lead):
        lead = paragraph.add_run(bold_lead)
        set_run_font(lead, bold=True)
        rest = paragraph.add_run(text[len(bold_lead):])
        set_run_font(rest)
    else:
        run = paragraph.add_run(text)
        set_run_font(run)
    return paragraph


def add_bullets(doc, items, level=0):
    for item in items:
        paragraph = doc.add_paragraph()
        paragraph.paragraph_format.left_indent = Inches(0.28 if level == 0 else 0.5)
        paragraph.paragraph_format.first_line_indent = Inches(-0.18)
        paragraph.paragraph_format.space_after = Pt(3)
        paragraph.paragraph_format.line_spacing = 1.12
        paragraph.paragraph_format.keep_together = True
        paragraph.paragraph_format.widow_control = True
        marker = paragraph.add_run("• ")
        set_run_font(marker, bold=True)
        run = paragraph.add_run(item)
        set_run_font(run)


def configure_styles(doc):
    styles = doc.styles
    normal = styles["Normal"]
    normal.font.name = "Aptos"
    normal._element.get_or_add_rPr().rFonts.set(qn("w:ascii"), "Aptos")
    normal._element.get_or_add_rPr().rFonts.set(qn("w:hAnsi"), "Aptos")
    normal.font.size = Pt(10.5)
    normal.font.color.rgb = RGBColor(0, 0, 0)
    normal.paragraph_format.space_after = Pt(5)
    normal.paragraph_format.line_spacing = 1.12

    title = styles["Title"]
    title.font.name = "Aptos Display"
    title._element.get_or_add_rPr().rFonts.set(qn("w:ascii"), "Aptos Display")
    title._element.get_or_add_rPr().rFonts.set(qn("w:hAnsi"), "Aptos Display")
    title.font.size = Pt(28)
    title.font.bold = True
    title.font.color.rgb = RGBColor(0, 0, 0)
    title.paragraph_format.space_after = Pt(12)
    title_ppr = title.element.get_or_add_pPr()
    title_border = title_ppr.find(qn("w:pBdr"))
    if title_border is not None:
        title_ppr.remove(title_border)

    subtitle = styles["Subtitle"]
    subtitle.font.name = "Aptos"
    subtitle._element.get_or_add_rPr().rFonts.set(qn("w:ascii"), "Aptos")
    subtitle._element.get_or_add_rPr().rFonts.set(qn("w:hAnsi"), "Aptos")
    subtitle.font.size = Pt(14)
    subtitle.font.color.rgb = RGBColor(0, 0, 0)

    for style_name, size in (("Heading 1", 18), ("Heading 2", 13), ("Heading 3", 11)):
        style = styles[style_name]
        style.font.name = "Aptos Display" if style_name != "Heading 3" else "Aptos"
        style._element.get_or_add_rPr().rFonts.set(qn("w:ascii"), style.font.name)
        style._element.get_or_add_rPr().rFonts.set(qn("w:hAnsi"), style.font.name)
        style.font.size = Pt(size)
        style.font.bold = True
        style.font.color.rgb = RGBColor(0, 0, 0)
        style.paragraph_format.keep_with_next = True
        style.paragraph_format.space_before = Pt(12 if style_name == "Heading 1" else 8)
        style.paragraph_format.space_after = Pt(5)

    for list_style in ("List Bullet", "List Bullet 2", "List Number"):
        style = styles[list_style]
        style.font.name = "Aptos"
        style._element.get_or_add_rPr().rFonts.set(qn("w:ascii"), "Aptos")
        style._element.get_or_add_rPr().rFonts.set(qn("w:hAnsi"), "Aptos")
        style.font.size = Pt(10.5)
        style.font.color.rgb = RGBColor(0, 0, 0)


def restore_spanish_accents(doc):
    replacements = {
        "Version": "Versión",
        "version": "versión",
        "Definicion": "Definición",
        "definicion": "definición",
        "orientacion": "orientación",
        "Orientacion": "Orientación",
        "linea": "línea",
        "Duracion": "Duración",
        "duracion": "duración",
        "Vision": "Visión",
        "vision": "visión",
        "accion": "acción",
        "Accion": "Acción",
        "Camara": "Cámara",
        "camara": "cámara",
        "exploracion": "exploración",
        "Exploracion": "Exploración",
        "presion": "presión",
        "Presion": "Presión",
        "recuperacion": "recuperación",
        "Recuperacion": "Recuperación",
        "interaccion": "interacción",
        "Interaccion": "Interacción",
        "Genero": "Género",
        "genero": "género",
        "Fantasia": "Fantasía",
        "fantasia": "fantasía",
        "tactil": "táctil",
        "Tactil": "Táctil",
        "tactiles": "táctiles",
        "Tactiles": "Táctiles",
        "evasion": "evasión",
        "Evasion": "Evasión",
        "inspiracion": "inspiración",
        "Inspiracion": "Inspiración",
        "actualizacion": "actualización",
        "Actualizacion": "Actualización",
        "demostracion": "demostración",
        "Demostracion": "Demostración",
        "relacion de aspecto": "relación de aspecto",
        "configuracion": "configuración",
        "Configuracion": "Configuración",
        "resolucion": "resolución",
        "Resolucion": "Resolución",
        "resoluciones": "resoluciones",
        "decision": "decisión",
        "Decision": "Decisión",
        "sesion": "sesión",
        "Sesion": "Sesión",
        "maximo": "máximo",
        "Maximo": "Máximo",
        "maximos": "máximos",
        "Maximos": "Máximos",
        "maximas": "máximas",
        "Maximas": "Máximas",
        "minimo": "mínimo",
        "Minimo": "Mínimo",
        "minima": "mínima",
        "Minima": "Mínima",
        "geometria": "geometría",
        "Geometria": "Geometría",
        "numero": "número",
        "Numero": "Número",
        "multiples": "múltiples",
        "Multiples": "Múltiples",
        "publico": "público",
        "Publico": "Público",
        "conexion": "conexión",
        "Conexion": "Conexión",
        "codigo": "código",
        "Codigo": "Código",
        "logica": "lógica",
        "Logica": "Lógica",
        "sincronizacion": "sincronización",
        "Sincronizacion": "Sincronización",
        "validacion": "validación",
        "Validacion": "Validación",
        "direccion": "dirección",
        "Direccion": "Dirección",
        "aparicion": "aparición",
        "Aparicion": "Aparición",
        "activacion": "activación",
        "Activacion": "Activación",
        "ubicacion": "ubicación",
        "Ubicacion": "Ubicación",
        "asignacion": "asignación",
        "Asignacion": "Asignación",
        "transicion": "transición",
        "Transicion": "Transición",
        "persecucion": "persecución",
        "Persecucion": "Persecución",
        "rapido": "rápido",
        "Rapido": "Rápido",
        "mas ": "más ",
        "esta ": "está ",
        "unicamente": "únicamente",
        "Unicamente": "Únicamente",
        "electrico": "eléctrico",
        "Electrico": "Eléctrico",
        "fisica": "física",
        "Fisica": "Física",
        "senal": "señal",
        "Senal": "Señal",
        "señalizacion": "señalización",
        "comunicacion": "comunicación",
        "Comunicacion": "Comunicación",
        "notificacion": "notificación",
        "Notificacion": "Notificación",
        "informacion": "información",
        "Informacion": "Información",
        "categoria": "categoría",
        "Categoria": "Categoría",
        "probabilidades": "probabilidades",
        "metrica": "métrica",
        "Metrica": "Métrica",
        "facil": "fácil",
        "Facil": "Fácil",
        "disminuye": "disminuye",
        "vision inmediata": "visión inmediata",
        "proxima": "próxima",
        "Proxima": "Próxima",
        "victoria provisional": "victoria provisional",
        "parametro": "parámetro",
        "Parametro": "Parámetro",
        "parametros": "parámetros",
        "Parametros": "Parámetros",
        "automatica": "automática",
        "Automatico": "Automático",
        "automaticos": "automáticos",
        "automaticamente": "automáticamente",
        "Automaticamente": "Automáticamente",
        "especifica": "específica",
        "Especifica": "Específica",
        "periodo": "período",
        "Periodo": "Período",
        "desconexion": "desconexión",
        "Desconexion": "Desconexión",
        "voluntariamente": "voluntariamente",
        "icono": "ícono",
        "Icono": "Ícono",
        "medica": "médica",
        "Medica": "Médica",
        "triangulo": "triángulo",
        "Triangulo": "Triángulo",
        "completarse": "completarse",
        "aceptacion": "aceptación",
        "Aceptacion": "Aceptación",
        "implementacion": "implementación",
        "Implementacion": "Implementación",
        "tacticas": "tácticas",
        "Tacticas": "Tácticas",
        "clasica": "clásica",
        "administracion": "administración",
        "Administracion": "Administración",
        "ambientacion": "ambientación",
        "Ambientacion": "Ambientación",
        "silueta": "silueta",
        "tecnicos": "técnicos",
        "Tecnicos": "Técnicos",
        "tecnica": "técnica",
        "Tecnica": "Técnica",
        "tecnicas": "técnicas",
        "Tecnicas": "Técnicas",
        "progresion": "progresión",
        "Progresion": "Progresión",
        "Decisiónes": "Decisiones",
        "creacion": "creación",
        "Creacion": "Creación",
        "union": "unión",
        "Union": "Unión",
        "energia": "energía",
        "Energia": "Energía",
        "ultima": "última",
        "Ultima": "Última",
        "limite": "límite",
        "Limite": "Límite",
        "maxima": "máxima",
        "Maxima": "Máxima",
        "mecanica": "mecánica",
        "Mecanica": "Mecánica",
        "mecanicas": "mecánicas",
        "Mecanicas": "Mecánicas",
        "dinamica": "dinámica",
        "Dinamica": "Dinámica",
        "condicion": "condición",
        "Condicion": "Condición",
        "tactica": "táctica",
        "Tactica": "Táctica",
        "tactico": "táctico",
        "Tactico": "Táctico",
        "tacticos": "tácticos",
        "Tacticos": "Tácticos",
        "tambien": "también",
        "Tambien": "También",
        "despues": "después",
        "Despues": "Después",
        "ademas": "además",
        "Ademas": "Además",
        "unico": "único",
        "Unico": "Único",
        "deberia": "debería",
        "Deberia": "Debería",
        "podra": "podrá",
        "Podra": "Podrá",
        "recibira": "recibirá",
        "Recibira": "Recibirá",
        "Quien este": "Quien esté",
        "preparacion": "preparación",
        "Preparacion": "Preparación",
        "interpolacion": "interpolación",
        "Interpolacion": "Interpolación",
        "repeticion": "repetición",
        "Repeticion": "Repetición",
        "migracion": "migración",
        "Migracion": "Migración",
        "reunen": "reúnen",
        "Reunen": "Reúnen",
        "algun": "algún",
        "Algun": "Algún",
        "corazon": "corazón",
        "Corazon": "Corazón",
        "seleccion": "selección",
        "Seleccion": "Selección",
        "boton": "botón",
        "Boton": "Botón",
        "opcion": "opción",
        "Opcion": "Opción",
        "curacion": "curación",
        "Curacion": "Curación",
        "reconexion": "reconexión",
        "Reconexion": "Reconexión",
        "cooperacion": "cooperación",
        "Cooperacion": "Cooperación",
        "presentacion": "presentación",
        "Presentacion": "Presentación",
        "iluminacion": "iluminación",
        "Iluminacion": "Iluminación",
        "formula": "fórmula",
        "Formula": "Fórmula",
        "caceria": "cacería",
        "Caceria": "Cacería",
        "limites": "límites",
        "Limites": "Límites",
        "bateria": "batería",
        "Bateria": "Batería",
        "catalogo": "catálogo",
        "Catalogo": "Catálogo",
        "sensacion": "sensación",
        "Sensacion": "Sensación",
        "tension": "tensión",
        "Tension": "Tensión",
        "intercepcion": "intercepción",
        "Intercepcion": "Intercepción",
        "mision": "misión",
        "Mision": "Misión",
        "envia": "envía",
        "Envia": "Envía",
    }

    def fix_paragraph(paragraph):
        for run in paragraph.runs:
            text = run.text
            for old, new in replacements.items():
                text = re.sub(rf"\b{re.escape(old)}\b", new, text)
            run.text = text

    for paragraph in doc.paragraphs:
        fix_paragraph(paragraph)
    for table in doc.tables:
        for row in table.rows:
            for cell in row.cells:
                for paragraph in cell.paragraphs:
                    fix_paragraph(paragraph)


def build_document():
    doc = Document()
    configure_styles(doc)
    section = doc.sections[0]
    section.page_width = Inches(8.5)
    section.page_height = Inches(11)
    section.top_margin = Inches(0.72)
    section.bottom_margin = Inches(0.72)
    section.left_margin = Inches(0.82)
    section.right_margin = Inches(0.82)

    title = doc.add_paragraph(style="Title")
    title.alignment = WD_ALIGN_PARAGRAPH.CENTER
    title.add_run("GDD Escape de Instituto Maldito")
    title_ppr = title._p.get_or_add_pPr()
    title_border = title_ppr.find(qn("w:pBdr"))
    if title_border is not None:
        title_ppr.remove(title_border)
    subtitle = doc.add_paragraph(style="Subtitle")
    subtitle.alignment = WD_ALIGN_PARAGRAPH.CENTER
    subtitle.add_run("Version 0.2 Alcance del MVP")

    add_table(
        doc,
        ["Campo", "Definicion"],
        [
            ["Estado", "Documento de diseño consolidado para el MVP"],
            ["Fecha", "17 de noviembre de 2026"],
            ["Plataforma principal", "Android API 25 o superior en orientacion horizontal"],
            ["Jugadores", "2 a 4 jugadores en linea"],
            ["Duracion objetivo", "5 a 15 minutos por partida"],
            ["Meta tecnica", "30 FPS estables en el dispositivo de referencia"],
        ],
        widths=[1.7, 4.9],
    )

    add_body(
        doc,
        "Este documento define el MVP de un juego cooperativo de supervivencia en el que un grupo de estudiantes debe escapar de un instituto controlado por una secta. La entrega valida una partida completa de seis salas, cooperación real por red, administración de recursos, secuestro y rescate, dificultad dinámica y una salida parcial o perfecta según quiénes sobrevivan.",
    )
    add_body(
        doc,
        "La prioridad es construir una experiencia jugable y verificable en Android. Las ideas que no sean necesarias para este ciclo se conservan como mejoras futuras y no condicionan la aceptación del MVP.",
    )

    add_heading(doc, "1 Vision del producto", 1)
    add_body(
        doc,
        "Escape de Instituto Maldito es un survival horror cooperativo de accion top down. De dos a cuatro estudiantes atraviesan habitaciones abiertas, buscan el panel que desbloquea la salida, administran flashes y vendas, y deciden cuando rescatar a compañeros raptados antes de que la actividad de la secta vuelva insostenible la partida.",
    )
    add_table(
        doc,
        ["Elemento", "Definicion del MVP"],
        [
            ["Genero", "Survival horror cooperativo con accion top down y evasion"],
            ["Fantasia", "Dos o mas estudiantes intentan escapar juntos de un instituto maldito"],
            ["Perspectiva", "Camara superior 2.5D"],
            ["Ritmo", "Alternancia entre calma, exploracion, preparacion, presion y recuperacion"],
            ["Objetivo", "Desbloquear la salida y sobrevivir a una secuencia de seis salas"],
            ["Inspiracion de control", "Movimiento tactil horizontal inspirado en Brawl Stars"],
        ],
        widths=[1.7, 4.9],
    )

    add_heading(doc, "2 Plataforma y objetivos tecnicos", 1)
    add_bullets(
        doc,
        [
            "Plataforma de lanzamiento del MVP: Android.",
            "iOS queda planificado como una actualizacion futura.",
            "PC se utiliza para desarrollo, pruebas y demostracion, pero no es la plataforma objetivo.",
            "Orientacion de pantalla: horizontal.",
            "Rendimiento inicial: 30 FPS estables.",
            "Dispositivo de referencia disponible: Honor Magic7 Lite.",
            "Version minima configurada: Android 7.1, nivel de API 25.",
            "Target SDK: automatico, resuelto por la version instalada del SDK de Android al compilar.",
            "Presupuesto provisional por frame: 33.3 ms como maximo para sostener 30 FPS.",
            "Presupuesto provisional de memoria: menos de 1 GB de uso pico durante una partida del MVP.",
            "Prueba termica provisional: 15 minutos continuos sin caidas graves de rendimiento ni cierre de la aplicacion.",
            "Los controles principales deben funcionar mediante pantalla tactil. Teclado y raton se mantienen como soporte de desarrollo.",
            "La interfaz debe adaptarse a diferentes relaciones de aspecto horizontales sin tapar el espacio jugable.",
            "La correccion del cambio entre ambos sentidos horizontales queda como tarea de baja prioridad y no bloquea el encadenado de salas ni las pruebas del Director y la IA.",
        ],
    )
    add_body(
        doc,
        "Los presupuestos de memoria y temperatura son limites preventivos estimados para el prototipo. Se reemplazaran por mediciones de Profiler y capturas del dispositivo cuando las seis salas, enemigos e interactuables esten integrados. La compilacion actual ya fue ejecutada satisfactoriamente en el Honor Magic7 Lite.",
    )

    add_heading(doc, "3 Alcance del MVP", 1)
    add_heading(doc, "3.1 Incluido", 2)
    add_bullets(
        doc,
        [
            "Partidas privadas para 2 a 4 jugadores reales.",
            "Creacion de partida y union mediante codigo.",
            "Seis salas por partida: Salon 1, Salon 2, Salon 3, Cafeteria 1, Cafeteria 2 y Cafeteria 3.",
            "Los salones utilizan una palanca y las cafeterias utilizan tres palancas obligatorias.",
            "Despues de completar las palancas, la puerta inicia una espera configurable. La implementacion validada utiliza 8 segundos y la siguiente prueba de estres utilizara temporalmente 30 segundos con mayor actividad del Director.",
            "Dos enemigos: Chiquito y Normal.",
            "Salud, estado vulnerable, arrastre, rapto, espectador y rescate posterior.",
            "Flash consumible, vendas, lockers, pickups y objetos soltados por jugadores.",
            "Panel electrico de emergencia que desbloquea la salida para todo el equipo.",
            "Director de dificultad con peligro base, actividad reciente y roles tacticos.",
            "Señales rapidas para cooperar sin voz.",
            "Victoria perfecta, victoria parcial y derrota total.",
            "Desconexion y reconexion con un periodo de gracia de 90 segundos.",
            "Estilo visual mixto: escenarios 3D con shaders y materiales, personajes, enemigos, objetos e items representados principalmente mediante elementos 2D.",
        ],
    )
    add_heading(doc, "3.2 Fuera de alcance", 2)
    add_bullets(
        doc,
        [
            "Version iOS.",
            "Bots en las partidas normales. El compañero del prototipo existe solo para pruebas.",
            "Habilidades por carrera, mejoras permanentes y aumento de vida maxima.",
            "Chat de voz, chat escrito y matchmaking publico.",
            "Barricadas complejas, escondites, inventario de cuadricula y clases de personaje.",
            "Jugador raptado convertido en enemigo controlado por la secta.",
            "Final narrativo definitivo y multiples dificultades seleccionables.",
        ],
    )

    add_heading(doc, "4 Arquitectura de sesion multijugador", 1)
    add_body(
        doc,
        "El MVP utiliza una arquitectura cliente servidor con un jugador como Host. La partida se publica en un lobby privado y los demas jugadores ingresan mediante un codigo. Relay facilita la conexion por Internet sin exponer puertos. La partida no comienza con menos de dos jugadores y admite un maximo de cuatro.",
    )
    add_bullets(
        doc,
        [
            "El cliente envia intenciones de movimiento e interaccion.",
            "El servidor valida daño, recursos, paneles, pickups, rescates y transiciones.",
            "La IA enemiga y el Director se ejecutan en el servidor.",
            "El movimiento local debe responder inmediatamente y reconciliarse con el servidor cuando sea necesario.",
            "Los jugadores remotos utilizan interpolacion visual.",
            "La vida, inventario y estado de captura pertenecen a cada jugador y se sincronizan.",
            "El estado de la salida, lockers y objetivos pertenece a la sesion o a la sala.",
            "Los jugadores que entran tarde o se reconectan reciben el estado actual, no una repeticion del historial de eventos.",
        ],
    )
    add_body(
        doc,
        "La desconexion del Host requiere una decision tecnica posterior: migracion de Host o cierre controlado de la sesion. Para el primer incremento de networking se permite cerrar la partida si el Host abandona, pero el riesgo debe registrarse en QA.",
    )

    add_heading(doc, "5 Flujo completo de una partida", 1)
    flow = [
        "El Host crea una partida privada y comparte el codigo.",
        "Entre dos y cuatro jugadores se marcan como listos.",
        "El Host inicia la partida y el servidor selecciona la primera sala.",
        "Los jugadores aparecen juntos y comienza una fase breve de calma.",
        "El equipo explora, abre lockers y administra sus recursos.",
        "El Director aumenta la actividad y asigna roles a los enemigos.",
        "El equipo localiza y activa la palanca del salon o las tres palancas de la cafeteria.",
        "La puerta inicia la espera configurada durante la cual el Director aumenta temporalmente la actividad. La version validada utiliza 8 segundos; la siguiente iteracion probara 30 segundos como escenario de supervivencia y estres.",
        "Al terminar la cuenta, la puerta cambia al material abierto, deja de bloquear el paso y los jugadores libres cruzan la salida. Quien este siendo arrastrado cuenta como raptado al abandonar la sala.",
        "La siguiente sala aparece con una zona de rescate visible si existe algun cautivo recuperable.",
        "El ciclo se repite hasta completar seis salas.",
        "Si todos son raptados, la partida termina en derrota y aparece un panel de resultado con las opciones Volver a jugar y Regresar al lobby.",
        "En la sexta sala, cada jugador puede escapar. Quienes escapen observan al resto hasta que se resuelva la partida.",
        "El resultado final distingue escape perfecto, escape parcial y derrota total, e incluye un memorial de los raptados.",
    ]
    for index, item in enumerate(flow, start=1):
        paragraph = doc.add_paragraph()
        paragraph.paragraph_format.left_indent = Inches(0.28)
        paragraph.paragraph_format.first_line_indent = Inches(-0.28)
        paragraph.paragraph_format.space_after = Pt(3)
        set_run_font(paragraph.add_run(f"{index}.  {item}"))

    add_heading(doc, "6 Jugador salud y captura", 1)
    add_table(
        doc,
        ["Estado", "Condicion", "Comportamiento"],
        [
            ["Normal", "2 o 3 corazones", "Puede moverse, interactuar, recoger objetos y usar flash."],
            ["Vulnerable", "1 corazon", "Conserva todas las acciones. El siguiente golpe inicia el arrastre."],
            ["Siendo arrastrado", "0 corazones", "Pierde el control mientras un enemigo lo lleva al portal mas cercano."],
            ["Raptado", "El enemigo alcanza el portal", "Entra en espectador y aparece encerrado en la siguiente sala."],
            ["Escapo", "Cruza la salida final", "Observa el desenlace de los jugadores restantes."],
        ],
        widths=[1.3, 1.5, 3.8],
        center_cols=[0, 1],
    )
    add_bullets(
        doc,
        [
            "La vida maxima inicial es 3 y debe permanecer configurable.",
            "Una venda restaura los 3 corazones y cada jugador puede llevar como maximo una.",
            "Un jugador liberado durante el arrastre vuelve con 1 corazon y 1.5 segundos de invulnerabilidad.",
            "La invulnerabilidad se comunica mediante parpadeo y bloquea daño y captura, pero no permite atravesar geometria.",
            "El jugador liberado conserva sus pilas y su venda.",
            "Si todos los jugadores quedan raptados, se muestra Game Over con las opciones Volver a jugar y Regresar al lobby.",
        ],
    )

    add_heading(doc, "7 Camara y flash", 1)
    add_body(
        doc,
        "El flash es el principal recurso defensivo. Cada pila permite un uso y cada jugador puede transportar hasta tres. El disparo consume una pila solo cuando el servidor acepta la accion.",
    )
    add_table(
        doc,
        ["Objetivo", "Efecto"],
        [
            ["Enemigo Chiquito", "Se destruye o desaparece de inmediato."],
            ["Enemigo Normal", "Queda aturdido durante 1.2 segundos."],
            ["Enemigo que arrastra", "Suelta al jugador y recibe el efecto correspondiente a su tipo."],
        ],
        widths=[2.2, 4.4],
    )
    add_body(
        doc,
        "El alcance, apertura del cono y tiempos de recarga se mantienen como parametros de balance pendientes de PlayMode. El servidor valida origen, direccion, alcance, pila disponible y objetivo antes de aplicar el resultado.",
    )

    add_heading(doc, "8 Objetivo de sala y salida", 1)
    add_body(
        doc,
        "La llave fisica se sustituye por palancas electricas de emergencia. Los salones requieren una palanca y las cafeterias requieren activar tres. Su ubicacion se elige entre puntos configurados para cada variante de sala.",
    )
    add_bullets(
        doc,
        [
            "La salida comienza bloqueada y utiliza el material de puerta cerrada; cada palanca requiere una interaccion canalizada de 1.5 segundos.",
            "Cada palanca solo puede activarse una vez y el HUD informa cuantas faltan.",
            "El servidor valida distancia, estado del jugador y disponibilidad de cada palanca.",
            "Al completar todas las palancas requeridas, comienza una espera configurable. La base validada es de 8 segundos y la prueba temporal de balance sera de 30 segundos.",
            "Durante la espera el Director aumenta temporalmente la actividad y el HUD muestra la cuenta regresiva.",
            "Al terminar, la puerta aplica el material de puerta abierta, desactiva su colision y permite completar la sala al cruzarla, sin exigir otra pulsacion tactil.",
        ],
    )

    add_heading(doc, "9 Lockers recursos e inventario", 1)
    add_heading(doc, "9.1 Apertura de lockers", 2)
    add_body(
        doc,
        "El jugador pulsa Interactuar una vez para iniciar la apertura. Puede alejarse mientras el locker procesa la accion. Despues de aproximadamente dos segundos, el recurso aparece en el suelo y cualquier jugador con capacidad puede recogerlo. El locker queda abierto y vacio hasta abandonar la sala.",
    )
    add_bullets(
        doc,
        [
            "Abrir varios lockers rapidamente aumenta la actividad oculta del jugador y la presion global.",
            "La cadena de aperturas se calcula por jugador dentro de una ventana configurable.",
            "El primer locker produce poca o ninguna penalizacion; las aperturas sucesivas generan mas actividad.",
            "La cantidad de objetos en el suelo no modifica el Director, porque es una metrica facil de manipular.",
            "Cada locker tiene estados persistentes: cerrado, abriendo, abierto con objeto y vacio.",
            "El sonido de apertura puede atraer o redirigir enemigos.",
        ],
    )
    add_heading(doc, "9.2 Modelo de recursos", 2)
    add_table(
        doc,
        ["Recurso", "Maximo", "Uso"],
        [
            ["Pila", "3 por jugador", "Cada unidad permite un flash."],
            ["Venda", "1 por jugador", "Restaura la vida hasta 3 corazones."],
            ["Panel de salida", "Estado de sala", "Desbloquea la salida para todo el equipo."],
        ],
        widths=[1.6, 1.6, 3.4],
        center_cols=[0, 1],
    )
    add_body(
        doc,
        "El inventario de pilas y vendas es personal. Un jugador puede soltar un recurso; el servidor lo descuenta y crea un pickup delante del personaje. Otro jugador puede recogerlo. Si el inventario esta lleno, el objeto permanece en el suelo. Todo lo abandonado desaparece al cambiar de sala.",
    )
    add_body(
        doc,
        "El loot combina muebles especializados, que garantizan una categoria, con muebles generales de contenido aleatorio. Las probabilidades pueden favorecer necesidades reales del equipo sin garantizar siempre el recurso ideal.",
    )

    add_heading(doc, "10 Director de dificultad", 1)
    add_body(
        doc,
        "El Director controla ritmo, apariciones y asignacion tactica. No utiliza una sola cifra interna: combina un peligro base permanente durante la partida con actividad reciente que disminuye si el equipo deja de generar ruido.",
    )
    add_body(
        doc,
        "La siguiente iteracion prioriza una prueba de estres durante la apertura de 30 segundos. El Director podra generar una oleada numerosa para medir presion, mientras la IA mejora la persecucion, el cierre de rutas y la capacidad de alcanzar, arrastrar y raptar al jugador sin apariciones injustas.",
    )
    add_table(
        doc,
        ["Componente", "Aumenta por", "Efecto"],
        [
            ["Peligro base", "Tiempo, salas superadas, rescates y jugadores raptados", "Reduce descansos y habilita tacticas mas coordinadas."],
            ["Actividad reciente", "Lockers en cadena, flashes, saqueo prolongado y rescates", "Adelanta oleadas y dirige enemigos a la zona activa."],
            ["Cautivos", "Cantidad de jugadores raptados", "Acelera el crecimiento de la dificultad y castiga abandonar al equipo."],
        ],
        widths=[1.5, 2.6, 2.5],
    )
    add_heading(doc, "10.1 Ritmo", 2)
    add_bullets(
        doc,
        [
            "Calma: permite reconocer la sala y organizarse.",
            "Preparacion: sonidos, luces o portales advierten que aumenta la actividad.",
            "Presion: aparecen enemigos y se asignan roles tacticos.",
            "Recuperacion: baja temporalmente la actividad despues de una oleada, rescate u objetivo importante.",
        ],
    )
    add_heading(doc, "10.2 Roles enemigos", 2)
    add_table(
        doc,
        ["Rol", "Prioridad"],
        [
            ["Perseguidor", "Mantener presion sobre el jugador mas cercano."],
            ["Interceptor", "Cerrar rutas hacia el panel o la salida."],
            ["Cazador de aislados", "Castigar al jugador que se separa para buscar o saquear."],
            ["Captor", "Priorizar jugadores vulnerables y proteger el arrastre."],
            ["Guardia", "Defender recursos, salidas y zonas de rescate."],
        ],
        widths=[2.0, 4.6],
    )
    add_body(
        doc,
        "En peligro bajo predomina la persecucion simple. En niveles altos el Director combina roles. Las apariciones deben respetar distancia minima, puntos configurados y telegraphing; no se permite crear enemigos directamente sobre el jugador o de forma injusta dentro de su campo inmediato.",
    )

    add_heading(doc, "11 Diseño de salas y progresion", 1)
    add_body(
        doc,
        "Una partida del MVP contiene seis salas. Los tres salones y las tres cafeterias reutilizan reglas y componentes, pero varian recorridos, mobiliario, palancas, lockers, portales e iluminacion.",
    )
    add_body(
        doc,
        "Estado actual: las distribuciones de Salon 1, Salon 2, Salon 3, Cafeteria 1, Cafeteria 2 y Cafeteria 3 estan construidas. Las tres cafeterias fueron recorridas y sus puertas, tres palancas, objetos y salidas funcionan. Se corrigieron colisiones faltantes en mesas; quedan ajustes visuales y de paredes que no bloquean la integracion funcional. Cafeteria 3 puede iniciar desde sus puntos de aparicion sin representar una entrada fisica.",
    )
    add_table(
        doc,
        ["Grupo", "Objetivo de contenido"],
        [
            ["Tres salones", "Salon 1, Salon 2 y Salon 3; cada uno utiliza una palanca."],
            ["Tres cafeterias", "Cafeteria 1, Cafeteria 2 y Cafeteria 3; cada una utiliza tres palancas."],
            ["Variante de rescate", "Zona visible y cerrada que puede integrarse en una sala grande."],
        ],
        widths=[2.0, 4.6],
    )
    add_bullets(
        doc,
        [
            "Los mapas son espacios abiertos para esquivar, rodear y atraer enemigos.",
            "Cada sala contiene spawns, salida, palancas, cobertura, lockers, pickups y puntos de aparicion. La entrada no necesita representacion visual en el prototipo.",
            "La ubicacion y activacion de elementos cambia entre configuraciones del mismo modulo.",
            "Las rutas, atajos, cuellos de botella y zonas sin aparicion deben documentarse en Miro antes de construir la escena final.",
            "Cafeteria 1 funciona como base tecnica para Cafeteria 2 y Cafeteria 3.",
        ],
    )

    add_heading(doc, "12 Rapto rescate y espectador", 1)
    add_body(
        doc,
        "Cuando el enemigo lleva a un jugador hasta el portal, el cautivo entra en modo espectador y puede alternar entre compañeros libres. En la siguiente sala aparece una zona de rescate totalmente visible. Los supervivientes deben invertir tiempo y asumir presion para abrirla.",
    )
    add_bullets(
        doc,
        [
            "El cautivo conserva pilas y venda.",
            "Al ser liberado vuelve con 1 corazon.",
            "La apertura de la zona de rescate tiene una duracion configurable pendiente de pruebas.",
            "La accion de rescate genera actividad para el Director.",
            "Si el equipo cambia de sala mientras un compañero esta siendo arrastrado, cuenta como raptado.",
            "En la sala final, quien no escape aparece en el memorial y no se genera una sala posterior de rescate.",
        ],
    )

    add_heading(doc, "13 Desconexion abandono y persistencia", 1)
    add_bullets(
        doc,
        [
            "Una desconexion involuntaria conserva vida, inventario y estado durante 90 segundos.",
            "Si regresa dentro del periodo, recupera su identidad y estado actual.",
            "Al superar 90 segundos, se marca como raptado permanentemente, sale de la cola de rescate y aparece en el memorial.",
            "Un jugador que abandona voluntariamente mientras tiene movilidad deja caer sus recursos personales.",
            "Un jugador que abandona estando raptado no deja recursos y se elimina de la cola de rescate.",
            "Los lockers abiertos, pickups recogidos y panel activado mantienen su estado para jugadores reconectados.",
        ],
    )

    add_heading(doc, "14 Señales y comunicacion", 1)
    add_body(
        doc,
        "El MVP no incluye voz ni chat escrito. La cooperacion utiliza una rueda radial accesible mientras el jugador se desplaza. Se mantiene pulsado el boton, se arrastra hacia una opcion y se suelta para enviar. Volver al centro cancela la seleccion.",
    )
    add_table(
        doc,
        ["Señal", "Uso", "Presentacion"],
        [
            ["Ayuda", "Solicitar apoyo inmediato.", "Rojo, exclamacion."],
            ["Necesito venda", "Comunicar falta de curacion.", "Verde, cruz medica."],
            ["Voy a rescatar", "Evitar acciones duplicadas.", "Azul, manos."],
            ["Salgamos", "Reunir al equipo en la salida.", "Amarillo, puerta."],
            ["Peligro", "Marcar una zona del escenario.", "Naranja, triangulo."],
            ["Voy al panel", "Comunicar que buscara o activara el objetivo.", "Celeste, palanca o rayo."],
        ],
        widths=[1.5, 3.0, 2.1],
    )
    add_body(
        doc,
        "Salida desbloqueada, jugador raptado, rescate disponible, desconexion y reconexion son mensajes automaticos del sistema. Las señales manuales tienen cooldown y desaparecen despues de pocos segundos. Cada una combina icono, color, texto y posicion fija para no depender solo del color.",
    )

    add_heading(doc, "15 Interfaz", 1)
    add_bullets(
        doc,
        [
            "Joystick izquierdo para movimiento en 360 grados.",
            "Controles derechos para flash, interaccion, venda y rueda de señales.",
            "HUD con corazones, pilas, venda, estado del objetivo y peligro.",
            "El peligro se comunica como un valor rojo acompañado por estados como Calma, Alerta y Caceria.",
            "Los jugadores raptados disponen de selector de espectador.",
            "El lobby muestra codigo, lista de 2 a 4 participantes, estado Listo y boton de inicio exclusivo del Host.",
        ],
    )

    add_heading(doc, "16 Balance inicial configurable", 1)
    add_table(
        doc,
        ["Parametro", "Valor inicial", "Estado"],
        [
            ["Vida maxima", "3 corazones", "Confirmado para MVP"],
            ["Pilas maximas", "3 por jugador", "Confirmado para MVP"],
            ["Vendas maximas", "1 por jugador", "Confirmado para MVP"],
            ["Flash por pila", "1", "Confirmado para MVP"],
            ["Stun enemigo Normal", "1.2 segundos", "Base del prototipo"],
            ["Activacion del panel", "1.5 segundos", "Valor inicial"],
            ["Apertura de la puerta", "8 s validados; prueba temporal de 30 s", "Configurable para balance y estres"],
            ["Aumento durante apertura", "+1.25 al multiplicador de peligro", "Estimacion configurable"],
            ["Apertura de locker", "2 segundos", "Valor inicial"],
            ["Invulnerabilidad al liberar", "1.5 segundos", "Valor inicial"],
            ["Reconexión", "90 segundos", "Confirmado para MVP"],
            ["Salas por partida", "6", "Confirmado para MVP"],
            ["Alcance y cono del flash", "Por definir", "Requiere PlayMode"],
            ["Duracion del rescate", "Por definir", "Requiere PlayMode"],
        ],
        widths=[2.3, 1.7, 2.6],
        center_cols=[1],
    )

    add_heading(doc, "17 Condiciones de resultado", 1)
    add_table(
        doc,
        ["Resultado", "Condicion"],
        [
            ["Escape perfecto", "Todos los jugadores escapan de la sexta sala."],
            ["Escape parcial", "Al menos un jugador escapa y uno o mas quedan raptados."],
            ["Derrota total", "Todos los jugadores quedan raptados antes de escapar."],
        ],
        widths=[2.0, 4.6],
    )
    add_body(
        doc,
        "El final narrativo definitivo se diseñara despues de validar la curva de dificultad. Para el MVP, completar seis salas es una condicion suficiente para probar una partida de principio a fin.",
    )

    add_heading(doc, "18 Criterios generales de aceptacion", 1)
    add_bullets(
        doc,
        [
            "La partida puede completarse de principio a fin con dos jugadores por Internet.",
            "La arquitectura admite hasta cuatro jugadores sin cambiar las reglas principales.",
            "El servidor es la fuente de verdad para daño, inventario, objetivos, rapto y salida.",
            "La primera sala guardada reemplaza la dependencia de geometria generada en runtime.",
            "La partida completa incluye seis transiciones de sala y un resultado final.",
            "El jugador puede abrir lockers, recoger, usar, soltar y compartir recursos.",
            "Los estados vulnerable, arrastrado, raptado y liberado son visibles y sincronizados.",
            "El Director alterna fases de ritmo y aumenta la presion al acumular cautivos.",
            "Al producirse una derrota, el panel de resultado permite iniciar otra partida o regresar al lobby.",
            "El juego mantiene 30 FPS estables en el dispositivo Android de referencia durante la captura de prueba acordada.",
            "Cada requisito funcional dispone de una tarjeta de implementacion y una prueba de aceptacion asociada.",
        ],
    )

    add_heading(doc, "18.1 Matriz de requisitos del MVP", 2)
    add_body(
        doc,
        "El estado [x] indica que existe una implementacion o decision documentada. El estado [ ] indica trabajo pendiente. Una implementacion marcada sigue necesitando ejecutar su prueba manual antes de cerrar QA.",
    )
    add_table(
        doc,
        ["Codigo", "Estado", "Requisito", "Prueba de aceptacion"],
        [
            ["PLAT-01", "[x]", "Compilar y ejecutar en Android horizontal.", "Abrir la APK en el Honor Magic7 Lite."],
            ["PLAT-02", "[x]", "Usar Android minimo API 25 y target SDK automatico.", "Revisar Player Settings y el manifiesto de la compilacion."],
            ["PERF-01", "[x]", "Mantener como objetivo 30 FPS y 33.3 ms por frame.", "Jugar 15 minutos en el dispositivo sin caidas graves."],
            ["SCOPE-01", "[x]", "Limitar el MVP a tres salones y tres cafeterias.", "La lista de contenido contiene exactamente seis salas."],
            ["FLOW-01", "[x]", "Documentar el flujo completo desde lobby hasta resultado.", "Recorrer los pasos de la seccion 5 sin contradicciones."],
            ["ARCH-01", "[x]", "Separar jugador, enemigos, inventario, salas, UI e interactuables.", "Los sistemas principales existen en componentes distintos."],
            ["ROOM-01", "[x]", "Disponer de Salon 1, Salon 2 y Salon 3 diseñados y probados.", "Recorrer cada salon sin atravesar ni quedar atrapado en geometria."],
            ["ROOM-02", "[x]", "Usar Cafeteria 1 como plantilla funcional.", "Validar spawns, palancas, salida e interactuables de Cafeteria 1."],
            ["ROOM-03", "[x]", "Integrar y probar Cafeteria 2 y Cafeteria 3 en Unity.", "Abrir, activar las tres palancas y completar ambas variantes desde el proyecto."],
            ["OBJ-01", "[x]", "Requerir una palanca en cada salon.", "Activar la palanca y observar el inicio de apertura."],
            ["OBJ-02", "[x]", "Requerir las tres palancas en cada cafeteria.", "Confirmar que una o dos no bastan y que la tercera inicia la apertura."],
            ["OBJ-03", "[x]", "Esperar el tiempo configurado con mayor peligro antes de abrir la salida.", "Comprobar la base validada de 8 segundos, la cuenta, la presion, el material y la colision."],
            ["MOVE-01", "[x]", "Mantener velocidad constante en movimiento cardinal y diagonal.", "Comparar recorridos rectos y diagonales durante el mismo tiempo."],
            ["COL-01", "[x]", "Evitar atravesamientos y enganches aparentes en los mapas actuales.", "Recorrer paredes, muebles, esquinas y puertas en PC y telefono."],
            ["CAP-01", "[x]", "Ocultar al aliado cuando es raptado.", "Dejar que llegue al portal y comprobar que ya no se renderiza."],
            ["INV-01", "[x]", "Permitir usar una venda con vida incompleta.", "Recibir daño, recoger una venda y recuperar tres corazones."],
            ["UI-01", "[x]", "Mostrar joystick, flash, interaccion, venda, objetivo y peligro.", "Verificar todos los controles en MobileHUD."],
            ["UI-02", "[ ]", "Aceptar ambos sentidos horizontales sin rotacion ciclica al sostener el telefono vertical.", "Iniciar y girar el telefono en sus cuatro posiciones; solo los dos horizontales deben cambiar la vista."],
            ["ART-01", "[x]", "Definir personajes, paredes, mobiliario y estilo 2D en Miro.", "Adjuntar el tablero o capturas como anexo del GDD."],
            ["ART-02", "[ ]", "Completar la mitad restante del estilo de salas, muebles, items, jugadores y zombis.", "Revisar las seis distribuciones con una direccion visual consistente y legible."],
            ["SESSION-01", "[ ]", "Encadenar las seis salas en una partida.", "Completar seis transiciones y mostrar el resultado final."],
            ["BAL-01", "[ ]", "Probar temporalmente una apertura de 30 segundos con aparicion masiva.", "Sobrevivir la cuenta, registrar presion y ajustar cantidad, ritmo y justicia de los spawns."],
            ["DIR-01", "[ ]", "Mejorar el Director y la IA para perseguir, cerrar rutas y capturar al jugador.", "Observar enemigos capaces de alcanzar, arrastrar y raptar sin aparecer injustamente encima del jugador."],
            ["NET-01", "[ ]", "Sincronizar reglas principales para dos a cuatro jugadores.", "Completar una partida de dos jugadores por Internet."],
        ],
        widths=[0.8, 0.65, 3.0, 2.15],
        center_cols=[0, 1],
    )

    add_heading(doc, "18.2 Lista de pruebas manuales", 2)
    add_table(
        doc,
        ["Prueba", "Plataforma", "Accion", "Resultado aprobado"],
        [
            ["QA-01", "PC y telefono", "Recorrer paredes, esquinas, muebles y puertas.", "No atraviesa ni queda enganchado."],
            ["QA-02", "PC y telefono", "Moverse el mismo tiempo en recto y diagonal.", "La velocidad percibida permanece constante."],
            ["QA-03", "Telefono", "Moverse mientras se apunta y dispara el flash.", "Los dos dedos se reconocen sin perder el joystick."],
            ["QA-04", "Telefono", "Recibir daño, recoger y usar una venda.", "La vida vuelve a tres y la venda se consume."],
            ["QA-05", "PC y telefono", "Permitir que el aliado sea llevado al portal.", "El aliado desaparece y aumenta el peligro."],
            ["QA-06", "PC y telefono", "Activar una palanca en un salon.", "Comienza la apertura de la puerta."],
            ["QA-07", "PC y telefono", "Activar una y dos palancas en una cafeteria.", "La salida permanece bloqueada."],
            ["QA-08", "PC y telefono", "Activar la tercera palanca.", "Inicia una cuenta de 8 segundos y aumenta el peligro."],
            ["QA-09", "PC y telefono", "Esperar la cuenta junto a la salida.", "Cambia a PuertaAbierta, desaparece la colision y se puede cruzar."],
            ["QA-10", "Telefono", "Sostener el dispositivo vertical y girarlo levemente.", "La pantalla permanece en una sola orientacion horizontal."],
            ["QA-11", "Telefono", "Jugar 15 minutos seguidos.", "No hay cierre, sobrecalentamiento grave ni degradacion evidente."],
            ["QA-12", "Unity", "Validar cada prefab de sala.", "No hay scripts perdidos, referencias nulas ni salidas sin configurar."],
            ["QA-13", "PC y telefono", "Configurar 30 segundos y activar la tercera palanca de una cafeteria.", "El Director genera una prueba de supervivencia intensa sin spawns injustos ni bloqueo inevitable."],
            ["QA-14", "PC y telefono", "Dejar que la IA persiga a un jugador vulnerable durante la oleada.", "El enemigo cierra distancia, inicia el arrastre y puede completar el rapto si no recibe ayuda."],
        ],
        widths=[0.8, 1.1, 2.7, 2.0],
        center_cols=[0, 1],
    )

    add_heading(doc, "19 Mejoras futuras", 1)
    add_bullets(
        doc,
        [
            "Version iOS.",
            "Tutorial individual y posibles bots de entrenamiento.",
            "Chat de voz y señales contextuales adicionales.",
            "Jugador desconectado convertido en cultista o enemigo.",
            "Habilidades de personajes por especialidad.",
            "Mejoras permanentes, incluida vida maxima.",
            "Barricadas, escondites, inventario de cuadricula y mas objetos de mision.",
            "Mas variantes de salas, finales narrativos y dificultades seleccionables.",
            "Matchmaking publico y opciones avanzadas de infraestructura.",
        ],
    )

    references_heading = add_heading(doc, "20 Referencias de diseño", 1)
    references_heading.paragraph_format.page_break_before = True
    add_body(
        doc,
        "Las siguientes obras orientan decisiones concretas, pero no convierten automaticamente todas sus mecanicas en requisitos del MVP.",
    )
    add_table(
        doc,
        ["Referencia", "Aporte"],
        [
            ["Brawl Stars", "Controles tactiles, lectura top down, espacios abiertos y rueda de señales."],
            ["Left 4 Dead", "Director de ritmo, avance por zonas, rescate y memoria de supervivientes."],
            ["Pac Man", "Intercepcion de rutas y roles enemigos legibles."],
            ["Alien Isolation", "Presion dinamica y sensacion de persecucion."],
            ["Dino Crisis", "Espacios cerrados y administracion limitada de recursos."],
            ["The Coma 2", "Ambientacion escolar e interactividad con mobiliario."],
            ["Dead by Daylight, Friday the 13th, Dont Starve y South Park", "Captura, rescate, tension cooperativa y referencia visual para el estilo 2D recortado."],
        ],
        widths=[2.2, 4.4],
    )

    add_heading(doc, "21 Decisiones pendientes", 1)
    add_bullets(
        doc,
        [
            "Conjunto final de resoluciones y relaciones de aspecto de aceptacion.",
            "Comportamiento de la sesion si el Host se desconecta.",
            "Duracion exacta de calma, recuperacion y apertura de la zona de rescate.",
            "Alcance, apertura y cooldown final del flash.",
            "Formula y limites del peligro base y de la actividad reciente.",
            "Presupuesto final de enemigos, objetos de red, memoria y bateria.",
        ],
    )

    doc.core_properties.title = "GDD Escape de Instituto Maldito"
    doc.core_properties.subject = "Alcance del MVP multijugador para Android"
    doc.core_properties.author = "Equipo de Escape de Instituto Maldito"
    doc.core_properties.keywords = "GDD, Unity, Android, multiplayer, MVP"
    restore_spanish_accents(doc)
    doc.save(OUTPUT)
    print(OUTPUT)


if __name__ == "__main__":
    build_document()
