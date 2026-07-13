using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace InventarioWeb.Pages;

public class ExportarCotizacionModel : PageBase
{
    // ── Valores fijos de la empresa ──────────────────────────
    private const string ElaboradoPor  = "Nombre del Responsable";
    private const string Correo        = "ventas@tuempresa.com";
    private const string Telefono      = "0000000000";
    private const string NombreEmpresa = "TU EMPRESA";
    private const string CargoFirmante = "Director General";

    private readonly IConfiguration    _config;
    private readonly IWebHostEnvironment _env;

    public ExportarCotizacionModel(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env    = env;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var deny = VerificarAcceso("cotizaciones");
        if (deny != null) return deny;

        if (Id <= 0)
            return RedirectToPage("/Cotizaciones");

        // ── 1. Cargar datos de BD ────────────────────────────
        CotizacionData? cot = null;
        var items = new List<ItemData>();

        var connStr = _config.GetConnectionString("SqlServer");
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand("dbo.sp_ObtenerCotizacionDetalle", conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = Id;

        await using var rdr = await cmd.ExecuteReaderAsync();
 
 
        if (await rdr.ReadAsync())
        {
            cot = new CotizacionData
            {
                Folio        = rdr.GetString(rdr.GetOrdinal("folio")),
                Fecha        = rdr.GetDateTime(rdr.GetOrdinal("fecha")),
                Cliente      = rdr.GetString(rdr.GetOrdinal("cliente")),
                Proyecto     = rdr.GetString(rdr.GetOrdinal("proyecto")),
                Rfq          = rdr.GetString(rdr.GetOrdinal("rfq")),
                DescuentoPct = rdr.GetDecimal(rdr.GetOrdinal("descuento_pct")),
                Solicitante  = rdr.GetString(rdr.GetOrdinal("solicitante")),
                Responsable  = rdr.GetString(rdr.GetOrdinal("responsable")),
                Consecutivo  = rdr.IsDBNull(rdr.GetOrdinal("consecutivo"))
                               ? null : rdr.GetInt32(rdr.GetOrdinal("consecutivo")),
            };
        }
        else
        {
            return RedirectToPage("/Cotizaciones");
        }

        if (await rdr.NextResultAsync())
        {
            var ordNoItem = rdr.GetOrdinal("no_item");
            var ordDesc   = rdr.GetOrdinal("descripcion");
            var ordCant   = rdr.GetOrdinal("cantidad");
            var ordUm     = rdr.GetOrdinal("unidad");
            var ordMarca  = rdr.GetOrdinal("marca");
            var ordMo     = rdr.GetOrdinal("mano_obra");
            var ordPu     = rdr.GetOrdinal("precio_unitario");
            var ordTotal  = rdr.GetOrdinal("total");
            var ordMoneda = rdr.GetOrdinal("moneda");
            var ordTe     = rdr.GetOrdinal("tiempo_entrega");

            while (await rdr.ReadAsync())
            {
                items.Add(new ItemData
                {
                    NoItem         = rdr.GetInt32(ordNoItem),
                    Descripcion    = rdr.GetString(ordDesc),
                    Cantidad       = rdr.GetDecimal(ordCant),
                    Unidad         = rdr.GetString(ordUm),
                    Marca          = rdr.IsDBNull(ordMarca) ? "" : rdr.GetString(ordMarca),
                    ManoObra       = rdr.GetDecimal(ordMo),
                    PrecioUnitario = rdr.GetDecimal(ordPu),
                    Total          = rdr.GetDecimal(ordTotal),
                    Moneda         = rdr.GetString(ordMoneda),
                    TiempoEntrega  = rdr.IsDBNull(ordTe) ? "" : rdr.GetString(ordTe),
                });
            }
        }

        // ── 2. Generar PDF con QuestPDF ───────────────────────
        QuestPDF.Settings.License = LicenseType.Community;

        var logoPath = Path.Combine(_env.WebRootPath, "images", "logo-tuempresa.png");

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(28, Unit.Point);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Arial"));

                // ENCABEZADO
                page.Header().Column(col =>
                {
                    // Fila 1: Logo | Título | Folio
                    col.Item().Row(row =>
                    {
                        // Logo izquierda
                        row.ConstantItem(90).Height(44).Element(logo =>
                        {
                            if (System.IO.File.Exists(logoPath))
                                logo.Image(logoPath).FitArea();
                            else
                                logo.AlignMiddle().AlignCenter()
                                    .Text(NombreEmpresa).Bold().FontSize(13).FontColor("#00d4aa");
                        });

                        // Título centro
                        row.RelativeItem().AlignCenter().AlignMiddle().Column(t =>
                        {
                            t.Item().AlignCenter().Text("Entrega de Cotización")
                                .Bold().FontSize(16).FontColor("#000000");
                            t.Item().PaddingTop(3).AlignCenter().Text(cot.Proyecto)
                                .Bold().FontSize(9).FontColor("#333333");
                        });

                        // Folio + Consecutivo derecha
                        row.ConstantItem(165).Border(1).BorderColor("#aaaaaa").Padding(5).Column(f =>
                        {
                            f.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Folio de cotizacion:").FontSize(7).FontColor("#666666");
                                r.RelativeItem().AlignRight().Text(cot.Folio).Bold().FontSize(8);
                            });
                            f.Item().PaddingTop(3).BorderTop(1).BorderColor("#dddddd").PaddingTop(3).Row(r =>
                            {
                                r.RelativeItem().Text("Consecutivo:").FontSize(7).FontColor("#666666");
                                r.RelativeItem().AlignRight()
                                    .Text(cot.Consecutivo.HasValue ? cot.Consecutivo.Value.ToString() : "—")
                                    .Bold().FontSize(8).FontColor("#1a5276");
                            });
                        });
                    });

                    col.Item().PaddingTop(6);

                    // Fila 2: Cliente (izq) | Elaborado por (der)
                    col.Item().Row(row =>
                    {
                        // Bloque cliente
                        row.RelativeItem().Border(1).BorderColor("#cccccc").Padding(6).Column(cl =>
                        {
                            cl.Item().Text(cot.Cliente).Bold().FontSize(10);
                            cl.Item().PaddingTop(2).Text(NombreEmpresa).FontSize(8).FontColor("#555555");
                            cl.Item().PaddingTop(4).Row(r =>
                            {
                                r.ConstantItem(50).Text("Correo").FontSize(7).FontColor("#888888");
                                r.RelativeItem().Text(Correo).FontSize(7).FontColor("#0070c0");
                            });
                            cl.Item().PaddingTop(1).Row(r =>
                            {
                                r.ConstantItem(50).Text("Teléfono").FontSize(7).FontColor("#888888");
                                r.RelativeItem().Text(Telefono).FontSize(7);
                            });
                            cl.Item().PaddingTop(1).Row(r =>
                            {
                                r.ConstantItem(50).Text("Solicitante").FontSize(7).FontColor("#888888");
                                r.RelativeItem().Text(!string.IsNullOrWhiteSpace(cot.Solicitante) ? cot.Solicitante : "—").FontSize(7);
                            });
                        });

                        row.ConstantItem(8);

                        // Bloque elaborado por
                        row.ConstantItem(165).Border(1).BorderColor("#cccccc").Column(tb =>
                        {
                            MetaRow(tb, "Responsable", !string.IsNullOrWhiteSpace(cot.Responsable) ? cot.Responsable : "—");
                            MetaRow(tb, "Fecha", cot.Fecha.ToString("dd/MM/yyyy"));
                            if (!string.IsNullOrWhiteSpace(cot.Rfq))
                                MetaRow(tb, "RFQ", cot.Rfq);
                        });
                    });

                    col.Item().PaddingTop(8);
                });

                // CONTENIDO
                page.Content().Column(col =>
                {
                    // Tabla de ítems
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(26);  // Item
                            cols.RelativeColumn(4);   // Descripción
                            cols.ConstantColumn(36);  // Cantidad
                            cols.ConstantColumn(30);  // Unidad
                            cols.ConstantColumn(50);  // Marca
                            cols.ConstantColumn(44);  // PU
                            cols.ConstantColumn(50);  // Total
                        });

                        // Header de tabla
                        table.Header(h =>
                        {
                            static IContainer HCell(IContainer cell) =>
                                cell.Background("#1a5276").Padding(3).AlignMiddle();

                            h.Cell().Element(HCell).Text("Ítem").FontColor(Colors.White).FontSize(7).Bold();
                            h.Cell().Element(HCell).Text("Descripción de material o equipo").FontColor(Colors.White).FontSize(7).Bold();
                            h.Cell().Element(HCell).AlignRight().Text("Cantidad").FontColor(Colors.White).FontSize(7).Bold();
                            h.Cell().Element(HCell).Text("Unidad").FontColor(Colors.White).FontSize(7).Bold();
                            h.Cell().Element(HCell).Text("Marca").FontColor(Colors.White).FontSize(7).Bold();
                            h.Cell().Element(HCell).AlignRight().Text("Precio unitario").FontColor(Colors.White).FontSize(7).Bold();
                            h.Cell().Element(HCell).AlignRight().Text("Total").FontColor(Colors.White).FontSize(7).Bold();
                        });

                        // Filas
                        bool alt = false;
                        foreach (var item in items)
                        {
                            if (item.Marca == "__PARTIDA__")
                            {
                                table.Cell().ColumnSpan(7)
                                    .Background("#1a5276")
                                    .Padding(5).AlignMiddle()
                                    .Text(item.Descripcion).FontColor(Colors.White).Bold().FontSize(8);
                                continue;
                            }

                            var bg = alt ? "#f5f5f5" : "#ffffff";
                            alt = !alt;

                            IContainer Cell(IContainer cell) =>
                                cell.Background(bg).BorderBottom(1).BorderColor("#e8e8e8").Padding(3).AlignMiddle();

                            table.Cell().Element(Cell).AlignCenter()
                                .Text(item.NoItem.ToString()).FontSize(7).Bold().FontColor("#1a5276");
                            table.Cell().Element(Cell).Text(item.Descripcion).FontSize(7);
                            table.Cell().Element(Cell).AlignRight()
                                .Text(item.Cantidad.ToString("N2")).FontSize(7);
                            table.Cell().Element(Cell).Text(item.Unidad).FontSize(7);
                            table.Cell().Element(Cell).Text(item.Marca).FontSize(7);
                            table.Cell().Element(Cell).AlignRight()
                                .Text("$" + item.PrecioUnitario.ToString("N2")).FontSize(7);
                            table.Cell().Element(Cell).AlignRight()
                                .Text("$" + item.Total.ToString("N2")).FontSize(7).Bold();
                        }
                    });

                    // Totales (excluir partidas)
                    decimal grandTotal = items.Where(i => i.Marca != "__PARTIDA__").Sum(i => i.Total);
                    decimal descuento  = cot.DescuentoPct > 0
                        ? Math.Round(grandTotal * cot.DescuentoPct / 100, 2) : 0;
                    decimal totalNeto  = grandTotal - descuento;

                    col.Item().PaddingTop(8).AlignRight().Column(totales =>
                    {
                        TotalRow(totales, "Costo Total de Proyecto:", "$" + grandTotal.ToString("N2"), bold: true, color: "#1a5276");

                        if (descuento > 0)
                        {
                            TotalRow(totales, $"Rebate {cot.DescuentoPct:N2}%:", "-$" + descuento.ToString("N2"), bold: true, color: "#c0392b");
                            TotalRow(totales, "Total de Proyecto con Descuentos:", "$" + totalNeto.ToString("N2"), bold: true, color: "#1a5276", large: true);
                        }
                    });

                    // Condiciones comerciales
                    col.Item().PaddingTop(14).Column(cond =>
                    {
                        cond.Item().Text("Condiciones Comerciales:").Bold().FontSize(8);
                        cond.Item().PaddingTop(3).Text(
                            "Los precios indicados están expresados en dólares americanos y no incluyen el IVA del 16%.")
                            .FontSize(7).FontColor("#333333");
                        cond.Item().PaddingTop(2).Text(
                            "Vigencia de la Propuesta: Esta propuesta tiene una validez por los siguientes 60 días.")
                            .FontSize(7).FontColor("#333333");
                        cond.Item().PaddingTop(2).Text(
                            "Forma de Pago: 30% de Anticipo y resto a plazo comercial una vez recibido el servicio prestado.")
                            .FontSize(7).FontColor("#333333");
                        cond.Item().PaddingTop(2).Text(
                            "Garantía de Materiales y Mano de Obra: Los materiales ofrecidos cuentan con un año de garantía " +
                            "contra defectos de fábrica, y un año de garantía de mano de obra por fallas de instalación, " +
                            "no incluye daños de usuarios o intervenciones de otro contratista.")
                            .FontSize(7).FontColor("#333333");
                        cond.Item().PaddingTop(2).Text(
                            "NOTA: La presente cotización solamente considera los conceptos solicitados, " +
                            "cualquier concepto adicional se cobrará por separado.")
                            .FontSize(7).FontColor("#c0392b").Italic();
                    });

                    // Firma
                    col.Item().PaddingTop(22).AlignCenter().Column(firma =>
                    {
                        firma.Item().AlignCenter()
                            .Text("Esperando que la presente información cumpla con los intereses de su compañía, " +
                                  "aprovecho la ocasión para enviarle un cordial saludo.")
                            .FontSize(7).FontColor("#555555").Italic();
                        firma.Item().PaddingTop(18).AlignCenter()
                            .Text("A t e n t a m e n t e").FontSize(9);
                        firma.Item().PaddingTop(22).AlignCenter()
                            .Text(ElaboradoPor).Bold().FontSize(9);
                        firma.Item().AlignCenter()
                            .Text(CargoFirmante).FontSize(8).FontColor("#555555");
                    });
                });

                // PIE DE PÁGINA
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Página ").FontSize(7).FontColor("#999999");
                    t.CurrentPageNumber().FontSize(7).FontColor("#999999");
                    t.Span(" de ").FontSize(7).FontColor("#999999");
                    t.TotalPages().FontSize(7).FontColor("#999999");
                });
            });
        }).GeneratePdf();

        return File(pdfBytes, "application/pdf", $"{cot.Folio}.pdf");
    }

    // ── Helpers ──────────────────────────────────────────────
    private static void MetaRow(ColumnDescriptor col, string label, string value, bool highlight = false)
    {
        col.Item()
            .BorderBottom(1).BorderColor("#eeeeee")
            .PaddingHorizontal(5).PaddingVertical(3)
            .Row(r =>
            {
                r.ConstantItem(72).Text(label).FontSize(7).FontColor("#888888");
                r.RelativeItem().AlignRight()
                    .Text(value).FontSize(7).Bold()
                    .FontColor(highlight ? "#1a5276" : "#000000");
            });
    }

    private static void TotalRow(ColumnDescriptor col, string label, string value,
        bool bold = false, string color = "#000000", bool large = false)
    {
        col.Item().PaddingVertical(1).Row(r =>
        {
            var labelCell = r.ConstantItem(170).AlignRight();
            if (bold) labelCell.Text(label).FontSize(8).Bold().FontColor(color);
            else       labelCell.Text(label).FontSize(8).FontColor(color);

            var valueCell = r.ConstantItem(85).AlignRight();
            if (bold) valueCell.Text(value).FontSize(large ? 9 : 8).Bold().FontColor(color);
            else       valueCell.Text(value).FontSize(large ? 9 : 8).FontColor(color);
        });
    }

    // ── Modelos internos ─────────────────────────────────────
    private sealed class CotizacionData
    {
        public string   Folio        { get; set; } = "";
        public DateTime Fecha        { get; set; }
        public string   Cliente      { get; set; } = "";
        public string   Proyecto     { get; set; } = "";
        public string   Rfq          { get; set; } = "";
        public decimal  DescuentoPct { get; set; }
        public string   Solicitante  { get; set; } = "";
        public string   Responsable  { get; set; } = "";
        public int?     Consecutivo  { get; set; }
    }

    private sealed class ItemData
    {
        public int     NoItem         { get; set; }
        public string  Descripcion    { get; set; } = "";
        public decimal Cantidad       { get; set; }
        public string  Unidad         { get; set; } = "";
        public string  Marca          { get; set; } = "";
        public decimal ManoObra       { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal Total          { get; set; }
        public string  Moneda         { get; set; } = "";
        public string  TiempoEntrega  { get; set; } = "";
    }
}
 