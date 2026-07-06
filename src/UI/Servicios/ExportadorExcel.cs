using ClosedXML.Excel;
using SWDataExtractor.Application.Servicios;
using SWDataExtractor.Data.Entities;
using SWDataExtractor.UI.ViewModels;

namespace SWDataExtractor.UI.Servicios;

public static class ExportadorExcel
{
    // Reportes del Explorador (F7a): duplicados, referencias rotas y posibles versiones,
    // cada uno en su hoja — es el formato que se comparte con el equipo.
    public static void ExportarReportesProyecto(
        IReadOnlyList<FilaDuplicado> duplicados,
        IReadOnlyList<ReferenciaRota> rotas,
        IReadOnlyList<FilaVersion> versiones,
        IReadOnlyList<IncumplimientoPropiedad> incumplimientos,
        string ruta)
    {
        using var wb = new XLWorkbook();

        var wsDup = wb.Worksheets.Add("Duplicados");
        EscribirEncabezados(wsDup, ["Grupo", "Nombre", "Tamaño (B)", "Última mod.", "Ruta"]);
        int fila = 2;
        foreach (var d in duplicados)
        {
            wsDup.Cell(fila, 1).Value = d.Grupo;
            wsDup.Cell(fila, 2).Value = d.Nombre;
            wsDup.Cell(fila, 3).Value = d.TamanoBytes;
            wsDup.Cell(fila, 4).Value = d.Fecha;
            wsDup.Cell(fila, 5).Value = d.Ruta;
            fila++;
        }
        wsDup.Columns().AdjustToContents();

        var wsRotas = wb.Worksheets.Add("Referencias rotas");
        EscribirEncabezados(wsRotas, ["Ensamble", "Referencia perdida", "Cantidad", "Ruta del ensamble"]);
        fila = 2;
        foreach (var r in rotas)
        {
            wsRotas.Cell(fila, 1).Value = r.EnsambleNombre;
            wsRotas.Cell(fila, 2).Value = r.RutaReferenciada;
            wsRotas.Cell(fila, 3).Value = r.Cantidad;
            wsRotas.Cell(fila, 4).Value = r.EnsambleRuta;
            fila++;
        }
        wsRotas.Columns().AdjustToContents();

        var wsVer = wb.Worksheets.Add("Posibles versiones");
        EscribirEncabezados(wsVer, ["Grupo", "Nombre base", "Más reciente", "Nombre", "Última mod.", "Ruta"]);
        fila = 2;
        foreach (var v in versiones)
        {
            wsVer.Cell(fila, 1).Value = v.Grupo;
            wsVer.Cell(fila, 2).Value = v.NombreBase;
            wsVer.Cell(fila, 3).Value = v.MasReciente ? "Sí" : "";
            wsVer.Cell(fila, 4).Value = v.Nombre;
            wsVer.Cell(fila, 5).Value = v.Fecha;
            wsVer.Cell(fila, 6).Value = v.Ruta;
            fila++;
        }
        wsVer.Columns().AdjustToContents();

        var wsCum = wb.Worksheets.Add("Cumplimiento");
        EscribirEncabezados(wsCum, ["Archivo", "Propiedad faltante", "Ruta"]);
        fila = 2;
        foreach (var c in incumplimientos)
        {
            wsCum.Cell(fila, 1).Value = c.Nombre;
            wsCum.Cell(fila, 2).Value = c.PropiedadFaltante;
            wsCum.Cell(fila, 3).Value = c.Ruta;
            fila++;
        }
        wsCum.Columns().AdjustToContents();

        wb.SaveAs(ruta);
    }

    private static void EscribirEncabezados(IXLWorksheet ws, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2b6cb0");
            cell.Style.Font.FontColor = XLColor.White;
        }
    }

    // BOM indentada + aplanada en un solo archivo con dos hojas.
    public static void ExportarBomCompleto(
        IReadOnlyList<ItemBom> indentado,
        IReadOnlyList<ItemBomAplanado> aplanado,
        string nombreEnsamble,
        string ruta)
    {
        using var wb = new XLWorkbook();
        AgregarHojaBomIndentado(wb, indentado);
        AgregarHojaBomAplanado(wb, aplanado);
        wb.SaveAs(ruta);
    }

    private static void AgregarHojaBomIndentado(XLWorkbook wb, IReadOnlyList<ItemBom> bom)
    {
        var ws = wb.Worksheets.Add("BOM Indentada");
        string[] headers = ["Nivel", "Nombre", "Tipo", "Configuración", "Cant. en padre", "Toolbox", "Envelope", "Suprimido", "Ruta"];
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2b6cb0");
            cell.Style.Font.FontColor = XLColor.White;
        }
        int fila = 2;
        foreach (var item in bom)
        {
            ws.Cell(fila, 1).Value = item.Nivel;
            ws.Cell(fila, 2).Value = new string(' ', item.Nivel * 2) + item.Nombre;
            ws.Cell(fila, 3).Value = item.Tipo;
            ws.Cell(fila, 4).Value = item.ConfiguracionUsada;
            ws.Cell(fila, 5).Value = item.CantidadEnPadre;
            ws.Cell(fila, 6).Value = item.EsToolbox   ? "Sí" : "No";
            ws.Cell(fila, 7).Value = item.EsEnvelope  ? "Sí" : "No";
            ws.Cell(fila, 8).Value = item.EsSuprimido ? "Sí" : "No";
            ws.Cell(fila, 9).Value = item.Ruta;
            if (item.EsSuprimido)
                ws.Row(fila).Style.Font.FontColor = XLColor.Gray;
            fila++;
        }
        ws.Columns().AdjustToContents();
        ws.Column(1).Width = 8;
    }

    private static void AgregarHojaBomAplanado(XLWorkbook wb, IReadOnlyList<ItemBomAplanado> bom)
    {
        var ws = wb.Worksheets.Add("BOM Aplanada");
        string[] headers = ["Nombre", "Tipo", "Cantidad Total", "Toolbox", "Ruta"];
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }
        int fila = 2;
        foreach (var item in bom)
        {
            ws.Cell(fila, 1).Value = item.Nombre;
            ws.Cell(fila, 2).Value = item.Tipo;
            ws.Cell(fila, 3).Value = item.CantidadTotal;
            ws.Cell(fila, 4).Value = item.EsToolbox ? "Sí" : "No";
            ws.Cell(fila, 5).Value = item.Ruta;
            fila++;
        }
        ws.Columns().AdjustToContents();
    }

    public static void ExportarPropiedades(
        IReadOnlyList<PropiedadVista> propiedades, string nombreArchivo, string ruta)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Propiedades");

        string[] headers = ["Componente", "Propiedad", "Configuración", "Valor", "Valor Resuelto", "Tipo", "Estándar", "Obligatoria"];
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }

        int fila = 2;
        foreach (var p in propiedades)
        {
            ws.Cell(fila, 1).Value = p.Componente ?? "(este archivo)";
            ws.Cell(fila, 2).Value = p.Nombre;
            ws.Cell(fila, 3).Value = p.Configuracion ?? "(documento)";
            ws.Cell(fila, 4).Value = p.Valor;
            ws.Cell(fila, 5).Value = p.ValorResuelto;
            ws.Cell(fila, 6).Value = p.Tipo;
            ws.Cell(fila, 7).Value = p.EsEstandar   ? "Sí" : "No";
            ws.Cell(fila, 8).Value = p.EsObligatoria ? "Sí" : "No";
            fila++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(ruta);
    }

    public static void ExportarRoscas(IReadOnlyList<Rosca> roscas, string ruta)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Roscas");

        string[] headers =
        [
            "Designación", "Estándar", "Tipo", "Ø Nominal (mm)",
            "Paso (mm)", "Hilos/pulg", "Prof. Rosca (mm)", "Prof. Barreno (mm)", "Pasante", "Cantidad"
        ];
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }

        int fila = 2;
        foreach (var r in roscas)
        {
            ws.Cell(fila, 1).Value = r.Designacion;
            ws.Cell(fila, 2).Value = r.Estandar;
            ws.Cell(fila, 3).Value = r.TipoBarreno;
            ws.Cell(fila, 4).Value = r.DiametroNominalMm;
            ws.Cell(fila, 5).Value = r.PasoMm;
            ws.Cell(fila, 6).Value = r.HilosPorPulgada;
            ws.Cell(fila, 7).Value = r.ProfundidadRoscaMm;
            ws.Cell(fila, 8).Value = r.ProfundidadBarrenoMm;
            ws.Cell(fila, 9).Value = r.Pasante ? "Sí" : "No";
            ws.Cell(fila, 10).Value = r.Cantidad;
            fila++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(ruta);
    }
}
