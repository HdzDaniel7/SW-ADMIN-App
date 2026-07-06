using Microsoft.EntityFrameworkCore;
using SWDataExtractor.Data.Entities;

namespace SWDataExtractor.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Archivo> Archivos => Set<Archivo>();
    public DbSet<Configuracion> Configuraciones => Set<Configuracion>();
    public DbSet<Propiedad> Propiedades => Set<Propiedad>();
    public DbSet<PropiedadFisica> PropiedadesFisicas => Set<PropiedadFisica>();
    public DbSet<Componente> Componentes => Set<Componente>();
    public DbSet<Feature> Features => Set<Feature>();
    public DbSet<Rosca> Roscas => Set<Rosca>();
    public DbSet<DiccionarioPropiedad> DiccionarioPropiedades => Set<DiccionarioPropiedad>();
    public DbSet<HistorialPropiedad> HistorialPropiedades => Set<HistorialPropiedad>();
    public DbSet<TrabajoExtraccion> TrabajosExtraccion => Set<TrabajoExtraccion>();
    public DbSet<Etiqueta> Etiquetas => Set<Etiqueta>();
    public DbSet<ArchivoEtiqueta> ArchivoEtiquetas => Set<ArchivoEtiqueta>();
    public DbSet<AjusteApp> AjustesApp => Set<AjusteApp>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        // ── archivos ──────────────────────────────────────────────────────────
        m.Entity<Archivo>(e =>
        {
            e.ToTable("archivos");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.Ruta).HasColumnName("ruta").IsRequired();
            e.Property(a => a.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(a => a.Tipo).HasColumnName("tipo").IsRequired();
            e.Property(a => a.HashSha256).HasColumnName("hash_sha256");
            e.Property(a => a.TamanoBytes).HasColumnName("tamano_bytes");
            e.Property(a => a.FechaModDisco).HasColumnName("fecha_mod_disco");
            e.Property(a => a.VersionSw).HasColumnName("version_sw");
            e.Property(a => a.Autor).HasColumnName("autor");
            e.Property(a => a.RutaPreview).HasColumnName("ruta_preview");
            e.Property(a => a.FechaExtrRapida).HasColumnName("fecha_extr_rapida");
            e.Property(a => a.FechaExtrProfunda).HasColumnName("fecha_extr_profunda");
            e.Property(a => a.EstadoRapido).HasColumnName("estado_rapido").IsRequired().HasDefaultValue("pendiente");
            e.Property(a => a.EstadoProfundo).HasColumnName("estado_profundo").IsRequired().HasDefaultValue("pendiente");
            e.Property(a => a.MensajeError).HasColumnName("mensaje_error");
            e.Property(a => a.Origen).HasColumnName("origen").IsRequired().HasDefaultValue("sistema_archivos");
            e.Property(a => a.DatosExtraJson).HasColumnName("datos_extra_json");

            e.HasIndex(a => a.Ruta).IsUnique();
            e.HasIndex(a => a.HashSha256);
            e.HasIndex(a => a.Tipo);
            e.HasIndex(a => a.EstadoRapido);
            e.HasIndex(a => a.EstadoProfundo);
        });

        // ── configuraciones ───────────────────────────────────────────────────
        m.Entity<Configuracion>(e =>
        {
            e.ToTable("configuraciones");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.ArchivoId).HasColumnName("archivo_id");
            e.Property(c => c.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(c => c.EsActiva).HasColumnName("es_activa");
            e.Property(c => c.EsDerivada).HasColumnName("es_derivada");

            e.HasIndex(c => new { c.ArchivoId, c.Nombre }).IsUnique();

            e.HasOne(c => c.Archivo)
             .WithMany(a => a.Configuraciones)
             .HasForeignKey(c => c.ArchivoId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── propiedades ───────────────────────────────────────────────────────
        m.Entity<Propiedad>(e =>
        {
            e.ToTable("propiedades");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.ArchivoId).HasColumnName("archivo_id");
            e.Property(p => p.ConfiguracionId).HasColumnName("configuracion_id");
            e.Property(p => p.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(p => p.Valor).HasColumnName("valor");
            e.Property(p => p.ValorResuelto).HasColumnName("valor_resuelto");
            e.Property(p => p.Tipo).HasColumnName("tipo");

            e.HasIndex(p => new { p.ArchivoId, p.ConfiguracionId, p.Nombre }).IsUnique();
            e.HasIndex(p => p.Nombre);

            e.HasOne(p => p.Archivo)
             .WithMany(a => a.Propiedades)
             .HasForeignKey(p => p.ArchivoId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(p => p.Configuracion)
             .WithMany(c => c.Propiedades)
             .HasForeignKey(p => p.ConfiguracionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── propiedades_fisicas ───────────────────────────────────────────────
        m.Entity<PropiedadFisica>(e =>
        {
            e.ToTable("propiedades_fisicas");
            e.HasKey(pf => pf.Id);
            e.Property(pf => pf.Id).HasColumnName("id");
            e.Property(pf => pf.ArchivoId).HasColumnName("archivo_id");
            e.Property(pf => pf.ConfiguracionId).HasColumnName("configuracion_id");
            e.Property(pf => pf.Material).HasColumnName("material");
            e.Property(pf => pf.DensidadKgM3).HasColumnName("densidad_kg_m3");
            e.Property(pf => pf.MasaKg).HasColumnName("masa_kg");
            e.Property(pf => pf.VolumenM3).HasColumnName("volumen_m3");
            e.Property(pf => pf.AreaM2).HasColumnName("area_m2");

            e.HasIndex(pf => new { pf.ArchivoId, pf.ConfiguracionId }).IsUnique();

            e.HasOne(pf => pf.Archivo)
             .WithMany(a => a.PropiedadesFisicas)
             .HasForeignKey(pf => pf.ArchivoId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(pf => pf.Configuracion)
             .WithMany(c => c.PropiedadesFisicas)
             .HasForeignKey(pf => pf.ConfiguracionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── componentes ───────────────────────────────────────────────────────
        m.Entity<Componente>(e =>
        {
            e.ToTable("componentes");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.EnsambleArchivoId).HasColumnName("ensamble_archivo_id");
            e.Property(c => c.EnsambleConfigId).HasColumnName("ensamble_config_id");
            e.Property(c => c.ComponenteArchivoId).HasColumnName("componente_archivo_id");
            e.Property(c => c.RutaReferenciada).HasColumnName("ruta_referenciada").IsRequired();
            e.Property(c => c.ConfiguracionUsada).HasColumnName("configuracion_usada");
            e.Property(c => c.Cantidad).HasColumnName("cantidad");
            e.Property(c => c.Suprimido).HasColumnName("suprimido");
            e.Property(c => c.EsToolbox).HasColumnName("es_toolbox");
            e.Property(c => c.EsEnvelope).HasColumnName("es_envelope");
            e.Property(c => c.DatosExtraJson).HasColumnName("datos_extra_json");

            e.HasIndex(c => new { c.EnsambleArchivoId, c.EnsambleConfigId, c.RutaReferenciada, c.ConfiguracionUsada, c.Suprimido }).IsUnique();
            e.HasIndex(c => c.EnsambleArchivoId);
            e.HasIndex(c => c.ComponenteArchivoId);

            e.HasOne(c => c.EnsambleArchivo)
             .WithMany(a => a.ComponentesComoEnsamble)
             .HasForeignKey(c => c.EnsambleArchivoId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.EnsambleConfig)
             .WithMany(cfg => cfg.ComponentesComoEnsamble)
             .HasForeignKey(c => c.EnsambleConfigId)
             .OnDelete(DeleteBehavior.NoAction);

            e.HasOne(c => c.ComponenteArchivo)
             .WithMany(a => a.ComponentesComoComponente)
             .HasForeignKey(c => c.ComponenteArchivoId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── features ─────────────────────────────────────────────────────────
        m.Entity<Feature>(e =>
        {
            e.ToTable("features");
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).HasColumnName("id");
            e.Property(f => f.ArchivoId).HasColumnName("archivo_id");
            e.Property(f => f.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(f => f.TipoSw).HasColumnName("tipo_sw").IsRequired();
            e.Property(f => f.Categoria).HasColumnName("categoria").IsRequired();
            e.Property(f => f.ParametrosJson).HasColumnName("parametros_json");
            e.Property(f => f.Suprimido).HasColumnName("suprimido");
            e.Property(f => f.Orden).HasColumnName("orden");

            e.HasIndex(f => f.ArchivoId);
            e.HasIndex(f => f.Categoria);
            e.HasIndex(f => f.TipoSw);

            e.HasOne(f => f.Archivo)
             .WithMany(a => a.Features)
             .HasForeignKey(f => f.ArchivoId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── roscas ────────────────────────────────────────────────────────────
        m.Entity<Rosca>(e =>
        {
            e.ToTable("roscas");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id");
            e.Property(r => r.ArchivoId).HasColumnName("archivo_id");
            e.Property(r => r.FeatureId).HasColumnName("feature_id");
            e.Property(r => r.Designacion).HasColumnName("designacion").IsRequired();
            e.Property(r => r.Estandar).HasColumnName("estandar");
            e.Property(r => r.TipoBarreno).HasColumnName("tipo_barreno");
            e.Property(r => r.DiametroNominalMm).HasColumnName("diametro_nominal_mm");
            e.Property(r => r.PasoMm).HasColumnName("paso_mm");
            e.Property(r => r.HilosPorPulgada).HasColumnName("hilos_por_pulgada");
            e.Property(r => r.ProfundidadRoscaMm).HasColumnName("profundidad_rosca_mm");
            e.Property(r => r.ProfundidadBarrenoMm).HasColumnName("profundidad_barreno_mm");
            e.Property(r => r.Pasante).HasColumnName("pasante");
            e.Property(r => r.Cantidad).HasColumnName("cantidad");

            e.HasIndex(r => r.Designacion);
            e.HasIndex(r => r.ArchivoId);

            e.HasOne(r => r.Archivo)
             .WithMany()
             .HasForeignKey(r => r.ArchivoId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(r => r.Feature)
             .WithMany(f => f.Roscas)
             .HasForeignKey(r => r.FeatureId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── diccionario_propiedades ───────────────────────────────────────────
        m.Entity<DiccionarioPropiedad>(e =>
        {
            e.ToTable("diccionario_propiedades");
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).HasColumnName("id");
            e.Property(d => d.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(d => d.Tipo).HasColumnName("tipo").IsRequired();
            e.Property(d => d.ValoresPermitidosJson).HasColumnName("valores_permitidos_json");
            e.Property(d => d.Obligatoria).HasColumnName("obligatoria");
            e.Property(d => d.Nivel).HasColumnName("nivel");
            e.Property(d => d.Descripcion).HasColumnName("descripcion");
            e.Property(d => d.Activa).HasColumnName("activa");

            e.HasIndex(d => d.Nombre).IsUnique();
        });

        // ── historial_propiedades ─────────────────────────────────────────────
        m.Entity<HistorialPropiedad>(e =>
        {
            e.ToTable("historial_propiedades");
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).HasColumnName("id");
            e.Property(h => h.LoteId).HasColumnName("lote_id").IsRequired();
            e.Property(h => h.ArchivoId).HasColumnName("archivo_id");
            e.Property(h => h.Configuracion).HasColumnName("configuracion");
            e.Property(h => h.Propiedad).HasColumnName("propiedad").IsRequired();
            e.Property(h => h.ValorAnterior).HasColumnName("valor_anterior");
            e.Property(h => h.ValorNuevo).HasColumnName("valor_nuevo");
            e.Property(h => h.Usuario).HasColumnName("usuario").IsRequired();
            e.Property(h => h.Fecha).HasColumnName("fecha").IsRequired();
            e.Property(h => h.Resultado).HasColumnName("resultado").IsRequired();

            e.HasIndex(h => h.LoteId);
            e.HasIndex(h => h.ArchivoId);

            e.HasOne(h => h.Archivo)
             .WithMany(a => a.HistorialPropiedades)
             .HasForeignKey(h => h.ArchivoId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── trabajos_extraccion ───────────────────────────────────────────────
        m.Entity<TrabajoExtraccion>(e =>
        {
            e.ToTable("trabajos_extraccion");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasColumnName("id");
            e.Property(t => t.ArchivoId).HasColumnName("archivo_id");
            e.Property(t => t.Tipo).HasColumnName("tipo").IsRequired();
            e.Property(t => t.Estado).HasColumnName("estado").IsRequired();
            e.Property(t => t.Intentos).HasColumnName("intentos").HasDefaultValue(0);
            e.Property(t => t.FechaEncolado).HasColumnName("fecha_encolado");
            e.Property(t => t.FechaInicio).HasColumnName("fecha_inicio");
            e.Property(t => t.FechaFin).HasColumnName("fecha_fin");
            e.Property(t => t.DuracionMs).HasColumnName("duracion_ms");
            e.Property(t => t.Mensaje).HasColumnName("mensaje");

            e.HasIndex(t => new { t.Estado, t.Tipo });

            e.HasOne(t => t.Archivo)
             .WithMany(a => a.TrabajosExtraccion)
             .HasForeignKey(t => t.ArchivoId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── etiquetas ─────────────────────────────────────────────────────────
        m.Entity<Etiqueta>(e =>
        {
            e.ToTable("etiquetas");
            e.HasKey(et => et.Id);
            e.Property(et => et.Id).HasColumnName("id");
            e.Property(et => et.Nombre).HasColumnName("nombre").IsRequired();
            e.Property(et => et.Color).HasColumnName("color");
            e.Property(et => et.Descripcion).HasColumnName("descripcion");
            e.Property(et => et.Activa).HasColumnName("activa");

            e.HasIndex(et => et.Nombre).IsUnique();
        });

        // ── archivo_etiquetas ─────────────────────────────────────────────────
        m.Entity<ArchivoEtiqueta>(e =>
        {
            e.ToTable("archivo_etiquetas");
            e.HasKey(ae => new { ae.ArchivoId, ae.EtiquetaId });
            e.Property(ae => ae.ArchivoId).HasColumnName("archivo_id");
            e.Property(ae => ae.EtiquetaId).HasColumnName("etiqueta_id");

            e.HasOne(ae => ae.Archivo)
             .WithMany(a => a.ArchivoEtiquetas)
             .HasForeignKey(ae => ae.ArchivoId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ae => ae.Etiqueta)
             .WithMany(et => et.ArchivoEtiquetas)
             .HasForeignKey(ae => ae.EtiquetaId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ajustes_app ───────────────────────────────────────────────────────
        m.Entity<AjusteApp>(e =>
        {
            e.ToTable("ajustes_app");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.Clave).HasColumnName("clave").IsRequired();
            e.Property(a => a.Valor).HasColumnName("valor");
            e.Property(a => a.Descripcion).HasColumnName("descripcion");

            e.HasIndex(a => a.Clave).IsUnique();
        });
    }
}
