using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace SetupHub.Janela.Kit;

static class Estilos
{
    public static Style BarraRolagem()
    {
        var P = K.P;
        string c(System.Windows.Media.SolidColorBrush b) => $"#{b.Color.A:X2}{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}";
        var xaml = $@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>
  <Setter Property='Width' Value='10'/>
  <Setter Property='MinWidth' Value='10'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Grid Background='Transparent'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Border x:Name='b' Background='{c(P.BordaForte)}' CornerRadius='3' Margin='2,2,2,2'/>
                    <ControlTemplate.Triggers>
                      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='{c(P.SubSel)}'/></Trigger>
                      <Trigger Property='IsDragging' Value='True'><Setter TargetName='b' Property='Background' Value='{c(P.Apagado)}'/></Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
  <Style.Triggers>
    <Trigger Property='Orientation' Value='Horizontal'>
      <Setter Property='Height' Value='10'/>
      <Setter Property='Width' Value='Auto'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ScrollBar'>
            <Track x:Name='PART_Track'>
              <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='{c(P.BordaForte)}' CornerRadius='3' Margin='2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
            </Track>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Trigger>
  </Style.Triggers>
</Style>";
        return (Style)XamlReader.Parse(xaml);
    }
}
