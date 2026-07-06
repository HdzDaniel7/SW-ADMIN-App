using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SWDataExtractor.Data.Migrations
{
    /// <inheritdoc />
    public partial class CimientosIniciales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ajustes_app",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    clave = table.Column<string>(type: "TEXT", nullable: false),
                    valor = table.Column<string>(type: "TEXT", nullable: true),
                    descripcion = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ajustes_app", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "archivos",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ruta = table.Column<string>(type: "TEXT", nullable: false),
                    nombre = table.Column<string>(type: "TEXT", nullable: false),
                    tipo = table.Column<string>(type: "TEXT", nullable: false),
                    hash_sha256 = table.Column<string>(type: "TEXT", nullable: true),
                    tamano_bytes = table.Column<long>(type: "INTEGER", nullable: true),
                    fecha_mod_disco = table.Column<string>(type: "TEXT", nullable: true),
                    version_sw = table.Column<int>(type: "INTEGER", nullable: true),
                    autor = table.Column<string>(type: "TEXT", nullable: true),
                    ruta_preview = table.Column<string>(type: "TEXT", nullable: true),
                    fecha_extr_rapida = table.Column<string>(type: "TEXT", nullable: true),
                    fecha_extr_profunda = table.Column<string>(type: "TEXT", nullable: true),
                    estado_rapido = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pendiente"),
                    estado_profundo = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pendiente"),
                    mensaje_error = table.Column<string>(type: "TEXT", nullable: true),
                    origen = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "sistema_archivos"),
                    datos_extra_json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archivos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "diccionario_propiedades",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    nombre = table.Column<string>(type: "TEXT", nullable: false),
                    tipo = table.Column<string>(type: "TEXT", nullable: false),
                    valores_permitidos_json = table.Column<string>(type: "TEXT", nullable: true),
                    obligatoria = table.Column<bool>(type: "INTEGER", nullable: false),
                    nivel = table.Column<string>(type: "TEXT", nullable: true),
                    descripcion = table.Column<string>(type: "TEXT", nullable: true),
                    activa = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_diccionario_propiedades", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "etiquetas",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    nombre = table.Column<string>(type: "TEXT", nullable: false),
                    color = table.Column<string>(type: "TEXT", nullable: true),
                    descripcion = table.Column<string>(type: "TEXT", nullable: true),
                    activa = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_etiquetas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "configuraciones",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    archivo_id = table.Column<int>(type: "INTEGER", nullable: false),
                    nombre = table.Column<string>(type: "TEXT", nullable: false),
                    es_activa = table.Column<bool>(type: "INTEGER", nullable: false),
                    es_derivada = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuraciones", x => x.id);
                    table.ForeignKey(
                        name: "FK_configuraciones_archivos_archivo_id",
                        column: x => x.archivo_id,
                        principalTable: "archivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "features",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    archivo_id = table.Column<int>(type: "INTEGER", nullable: false),
                    nombre = table.Column<string>(type: "TEXT", nullable: false),
                    tipo_sw = table.Column<string>(type: "TEXT", nullable: false),
                    categoria = table.Column<string>(type: "TEXT", nullable: false),
                    parametros_json = table.Column<string>(type: "TEXT", nullable: true),
                    suprimido = table.Column<bool>(type: "INTEGER", nullable: false),
                    orden = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_features", x => x.id);
                    table.ForeignKey(
                        name: "FK_features_archivos_archivo_id",
                        column: x => x.archivo_id,
                        principalTable: "archivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "historial_propiedades",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    lote_id = table.Column<string>(type: "TEXT", nullable: false),
                    archivo_id = table.Column<int>(type: "INTEGER", nullable: false),
                    configuracion = table.Column<string>(type: "TEXT", nullable: true),
                    propiedad = table.Column<string>(type: "TEXT", nullable: false),
                    valor_anterior = table.Column<string>(type: "TEXT", nullable: true),
                    valor_nuevo = table.Column<string>(type: "TEXT", nullable: true),
                    usuario = table.Column<string>(type: "TEXT", nullable: false),
                    fecha = table.Column<string>(type: "TEXT", nullable: false),
                    resultado = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historial_propiedades", x => x.id);
                    table.ForeignKey(
                        name: "FK_historial_propiedades_archivos_archivo_id",
                        column: x => x.archivo_id,
                        principalTable: "archivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trabajos_extraccion",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    archivo_id = table.Column<int>(type: "INTEGER", nullable: false),
                    tipo = table.Column<string>(type: "TEXT", nullable: false),
                    estado = table.Column<string>(type: "TEXT", nullable: false),
                    intentos = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    fecha_encolado = table.Column<string>(type: "TEXT", nullable: true),
                    fecha_inicio = table.Column<string>(type: "TEXT", nullable: true),
                    fecha_fin = table.Column<string>(type: "TEXT", nullable: true),
                    duracion_ms = table.Column<long>(type: "INTEGER", nullable: true),
                    mensaje = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trabajos_extraccion", x => x.id);
                    table.ForeignKey(
                        name: "FK_trabajos_extraccion_archivos_archivo_id",
                        column: x => x.archivo_id,
                        principalTable: "archivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "archivo_etiquetas",
                columns: table => new
                {
                    archivo_id = table.Column<int>(type: "INTEGER", nullable: false),
                    etiqueta_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archivo_etiquetas", x => new { x.archivo_id, x.etiqueta_id });
                    table.ForeignKey(
                        name: "FK_archivo_etiquetas_archivos_archivo_id",
                        column: x => x.archivo_id,
                        principalTable: "archivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_archivo_etiquetas_etiquetas_etiqueta_id",
                        column: x => x.etiqueta_id,
                        principalTable: "etiquetas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "componentes",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ensamble_archivo_id = table.Column<int>(type: "INTEGER", nullable: false),
                    ensamble_config_id = table.Column<int>(type: "INTEGER", nullable: false),
                    componente_archivo_id = table.Column<int>(type: "INTEGER", nullable: true),
                    ruta_referenciada = table.Column<string>(type: "TEXT", nullable: false),
                    configuracion_usada = table.Column<string>(type: "TEXT", nullable: true),
                    cantidad = table.Column<int>(type: "INTEGER", nullable: false),
                    suprimido = table.Column<bool>(type: "INTEGER", nullable: false),
                    es_toolbox = table.Column<bool>(type: "INTEGER", nullable: false),
                    es_envelope = table.Column<bool>(type: "INTEGER", nullable: false),
                    datos_extra_json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_componentes", x => x.id);
                    table.ForeignKey(
                        name: "FK_componentes_archivos_componente_archivo_id",
                        column: x => x.componente_archivo_id,
                        principalTable: "archivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_componentes_archivos_ensamble_archivo_id",
                        column: x => x.ensamble_archivo_id,
                        principalTable: "archivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_componentes_configuraciones_ensamble_config_id",
                        column: x => x.ensamble_config_id,
                        principalTable: "configuraciones",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "propiedades",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    archivo_id = table.Column<int>(type: "INTEGER", nullable: false),
                    configuracion_id = table.Column<int>(type: "INTEGER", nullable: true),
                    nombre = table.Column<string>(type: "TEXT", nullable: false),
                    valor = table.Column<string>(type: "TEXT", nullable: true),
                    valor_resuelto = table.Column<string>(type: "TEXT", nullable: true),
                    tipo = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_propiedades", x => x.id);
                    table.ForeignKey(
                        name: "FK_propiedades_archivos_archivo_id",
                        column: x => x.archivo_id,
                        principalTable: "archivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_propiedades_configuraciones_configuracion_id",
                        column: x => x.configuracion_id,
                        principalTable: "configuraciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "propiedades_fisicas",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    archivo_id = table.Column<int>(type: "INTEGER", nullable: false),
                    configuracion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    material = table.Column<string>(type: "TEXT", nullable: true),
                    densidad_kg_m3 = table.Column<double>(type: "REAL", nullable: true),
                    masa_kg = table.Column<double>(type: "REAL", nullable: true),
                    volumen_m3 = table.Column<double>(type: "REAL", nullable: true),
                    area_m2 = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_propiedades_fisicas", x => x.id);
                    table.ForeignKey(
                        name: "FK_propiedades_fisicas_archivos_archivo_id",
                        column: x => x.archivo_id,
                        principalTable: "archivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_propiedades_fisicas_configuraciones_configuracion_id",
                        column: x => x.configuracion_id,
                        principalTable: "configuraciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "roscas",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    archivo_id = table.Column<int>(type: "INTEGER", nullable: false),
                    feature_id = table.Column<int>(type: "INTEGER", nullable: false),
                    designacion = table.Column<string>(type: "TEXT", nullable: false),
                    estandar = table.Column<string>(type: "TEXT", nullable: true),
                    tipo_barreno = table.Column<string>(type: "TEXT", nullable: true),
                    diametro_nominal_mm = table.Column<double>(type: "REAL", nullable: true),
                    paso_mm = table.Column<double>(type: "REAL", nullable: true),
                    hilos_por_pulgada = table.Column<double>(type: "REAL", nullable: true),
                    profundidad_rosca_mm = table.Column<double>(type: "REAL", nullable: true),
                    profundidad_barreno_mm = table.Column<double>(type: "REAL", nullable: true),
                    pasante = table.Column<bool>(type: "INTEGER", nullable: false),
                    cantidad = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roscas", x => x.id);
                    table.ForeignKey(
                        name: "FK_roscas_archivos_archivo_id",
                        column: x => x.archivo_id,
                        principalTable: "archivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_roscas_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ajustes_app_clave",
                table: "ajustes_app",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_archivo_etiquetas_etiqueta_id",
                table: "archivo_etiquetas",
                column: "etiqueta_id");

            migrationBuilder.CreateIndex(
                name: "IX_archivos_estado_profundo",
                table: "archivos",
                column: "estado_profundo");

            migrationBuilder.CreateIndex(
                name: "IX_archivos_estado_rapido",
                table: "archivos",
                column: "estado_rapido");

            migrationBuilder.CreateIndex(
                name: "IX_archivos_hash_sha256",
                table: "archivos",
                column: "hash_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_archivos_ruta",
                table: "archivos",
                column: "ruta",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_archivos_tipo",
                table: "archivos",
                column: "tipo");

            migrationBuilder.CreateIndex(
                name: "IX_componentes_componente_archivo_id",
                table: "componentes",
                column: "componente_archivo_id");

            migrationBuilder.CreateIndex(
                name: "IX_componentes_ensamble_archivo_id",
                table: "componentes",
                column: "ensamble_archivo_id");

            migrationBuilder.CreateIndex(
                name: "IX_componentes_ensamble_archivo_id_ensamble_config_id_ruta_referenciada_configuracion_usada_suprimido",
                table: "componentes",
                columns: new[] { "ensamble_archivo_id", "ensamble_config_id", "ruta_referenciada", "configuracion_usada", "suprimido" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_componentes_ensamble_config_id",
                table: "componentes",
                column: "ensamble_config_id");

            migrationBuilder.CreateIndex(
                name: "IX_configuraciones_archivo_id_nombre",
                table: "configuraciones",
                columns: new[] { "archivo_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_diccionario_propiedades_nombre",
                table: "diccionario_propiedades",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_etiquetas_nombre",
                table: "etiquetas",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_features_archivo_id",
                table: "features",
                column: "archivo_id");

            migrationBuilder.CreateIndex(
                name: "IX_features_categoria",
                table: "features",
                column: "categoria");

            migrationBuilder.CreateIndex(
                name: "IX_features_tipo_sw",
                table: "features",
                column: "tipo_sw");

            migrationBuilder.CreateIndex(
                name: "IX_historial_propiedades_archivo_id",
                table: "historial_propiedades",
                column: "archivo_id");

            migrationBuilder.CreateIndex(
                name: "IX_historial_propiedades_lote_id",
                table: "historial_propiedades",
                column: "lote_id");

            migrationBuilder.CreateIndex(
                name: "IX_propiedades_archivo_id_configuracion_id_nombre",
                table: "propiedades",
                columns: new[] { "archivo_id", "configuracion_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_propiedades_configuracion_id",
                table: "propiedades",
                column: "configuracion_id");

            migrationBuilder.CreateIndex(
                name: "IX_propiedades_nombre",
                table: "propiedades",
                column: "nombre");

            migrationBuilder.CreateIndex(
                name: "IX_propiedades_fisicas_archivo_id_configuracion_id",
                table: "propiedades_fisicas",
                columns: new[] { "archivo_id", "configuracion_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_propiedades_fisicas_configuracion_id",
                table: "propiedades_fisicas",
                column: "configuracion_id");

            migrationBuilder.CreateIndex(
                name: "IX_roscas_archivo_id",
                table: "roscas",
                column: "archivo_id");

            migrationBuilder.CreateIndex(
                name: "IX_roscas_designacion",
                table: "roscas",
                column: "designacion");

            migrationBuilder.CreateIndex(
                name: "IX_roscas_feature_id",
                table: "roscas",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "IX_trabajos_extraccion_archivo_id",
                table: "trabajos_extraccion",
                column: "archivo_id");

            migrationBuilder.CreateIndex(
                name: "IX_trabajos_extraccion_estado_tipo",
                table: "trabajos_extraccion",
                columns: new[] { "estado", "tipo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ajustes_app");

            migrationBuilder.DropTable(
                name: "archivo_etiquetas");

            migrationBuilder.DropTable(
                name: "componentes");

            migrationBuilder.DropTable(
                name: "diccionario_propiedades");

            migrationBuilder.DropTable(
                name: "historial_propiedades");

            migrationBuilder.DropTable(
                name: "propiedades");

            migrationBuilder.DropTable(
                name: "propiedades_fisicas");

            migrationBuilder.DropTable(
                name: "roscas");

            migrationBuilder.DropTable(
                name: "trabajos_extraccion");

            migrationBuilder.DropTable(
                name: "etiquetas");

            migrationBuilder.DropTable(
                name: "configuraciones");

            migrationBuilder.DropTable(
                name: "features");

            migrationBuilder.DropTable(
                name: "archivos");
        }
    }
}
