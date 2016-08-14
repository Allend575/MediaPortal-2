<?xml version="1.0" encoding="utf-8"?>
<Grid
  xmlns="www.team-mediaportal.com/2008/mpf/directx"
  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
  xmlns:fanart="clr-namespace:MediaPortal.Extensions.UserServices.FanArtService.Client;assembly=FanArtService.Client"
  xmlns:media="clr-namespace:MediaPortal.UiComponents.Media.Models.AspectWrappers;assembly=Media"
  Grid.Column="0" Grid.Row="0" x:Name="MediaItemDetails" Margin="0,30,20,-200"
  HorizontalAlignment="Stretch" VerticalAlignment="Stretch">

  <Grid.Resources>
    <fanart:FanArtImageSourceConverter x:Key="FanArtImageSourceConverter"/>
  </Grid.Resources>
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="*"/>
  </Grid.RowDefinitions>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="540"/>
    <ColumnDefinition Width="*"/>
  </Grid.ColumnDefinitions>

  <Image Grid.Column="1" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" Stretch="UniformToFill" Margin="0,10,10,0" Height="210">
    <Image.Source>
      <ImageSourceWrapper Delay="0.5" DelayInOut="true" Transition="transitions\dissolve" TransitionDuration="0.5"
                          Source="{Binding Source={StaticResource FanArtBackgroundModel},Path=MediaItem}">
      </ImageSourceWrapper>
    </Image.Source>
    <Image.OpacityMask>
      <VisualBrush>
        <VisualBrush.Visual>
          <Border Name="mask" Background="White" CornerRadius="6" BorderThickness="0" BorderBrush="Black"/>
        </VisualBrush.Visual>
      </VisualBrush>
    </Image.OpacityMask>
  </Image>

  <media:SeasonAspectWrapper MediaItem="{Binding Source={StaticResource FanArtBackgroundModel},Path=MediaItem}"
                              Grid.Column="0" Grid.Row="0" Margin="0,0,10,0">
    <media:SeasonAspectWrapper.Template>
      <ControlTemplate>
        <StackPanel Orientation="Vertical" HorizontalAlignment="Stretch" VerticalAlignment="Top">
          <Label x:Name="SeriesNameLabel" Color="{ThemeResource TextColor}" FontSize="{ThemeResource SmallFontSize}" Wrap="True" FontFamily="SegoeUIBold"
                 DataContext="{Binding RelativeSource={RelativeSource TemplatedParent}}" TextTrimming="WordEllipsis">
            <Label.Content>
              <Binding Path="SeriesName" Converter="{StaticResource StringFormatConverter}" ConverterParameter="{}{0}"/>
            </Label.Content>
          </Label>
          <StackPanel Orientation="Horizontal">
            <Label Content="[Media.SeasonRootViewName]" Color="{ThemeResource TextColor}" FontSize="{ThemeResource SmallFontSize}"/>
            <Label x:Name="SeasonLabel" Color="{ThemeResource TextColor}" FontSize="{ThemeResource SmallFontSize}" Margin="7,0,0,0"
                 DataContext="{Binding RelativeSource={RelativeSource TemplatedParent}}" Content="{Binding Season}"/>
          </StackPanel>
          <StackPanel Orientation="Horizontal" DataContext="{Binding Source={StaticResource FanArtBackgroundModel},Path=SelectedItem}">
            <Label Color="{ThemeResource TextColor}" FontSize="{ThemeResource SmallFontSize}" Content="{Binding AvailableEpisodes}"
                   Margin="0,0,8,0" Opacity="0.5"/>
            <Label Color="{ThemeResource TextColor}" FontSize="{ThemeResource SmallFontSize}" Content="[Media.EpisodeItemsHeader]"
                   Margin="0,0,8,0" Opacity="0.5"/>
          </StackPanel>
        </StackPanel>
      </ControlTemplate>
    </media:SeasonAspectWrapper.Template>
</media:SeasonAspectWrapper>

  <StackPanel Orientation="Vertical" Grid.Column="0" Grid.ColumnSpan="2" Grid.Row="1" Margin="0,0,0,5">
    <!--media:VideoAspectWrapper MediaItem="{Binding Source={StaticResource FanArtBackgroundModel},Path=MediaItem}">
      <media:VideoAspectWrapper.Template>
        <ControlTemplate>
          <Grid DataContext="{Binding RelativeSource={RelativeSource TemplatedParent}}">
            <Label x:Name="Genre" Opacity="0.5" FontSize="{ThemeResource SmallFontSize}"
                   Content="{Binding Genres}" Color="{ThemeResource TextColor}" />
          </Grid>
        </ControlTemplate>
      </media:VideoAspectWrapper.Template>
    </media:VideoAspectWrapper-->
    
  </StackPanel>

  <media:SeasonAspectWrapper MediaItem="{Binding Source={StaticResource FanArtBackgroundModel},Path=MediaItem}"
                             Grid.Column="0" Grid.ColumnSpan="2" Grid.Row="2" Margin="0,30,0,10" Height="440">
    <media:SeasonAspectWrapper.Template>
      <ControlTemplate>
        <Grid>
          <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
          </Grid.RowDefinitions>
          <Label x:Name="Description" DataContext="{Binding RelativeSource={RelativeSource TemplatedParent}}" Grid.Row="1"
                   Wrap="True" Opacity="0.5" TextTrimming="WordEllipsis" FontSize="{ThemeResource SmallFontSize}" Margin="0,5,5,0"
                   HorizontalAlignment="Stretch" Content="{Binding Source={StaticResource FanArtBackgroundModel}, Path=Description}"
                   Color="{ThemeResource TextColor}"/>
        </Grid>
      </ControlTemplate>
    </media:SeasonAspectWrapper.Template>
  </media:SeasonAspectWrapper>

  <!--media:MediaAspectWrapper MediaItem="{Binding Source={StaticResource FanArtBackgroundModel},Path=MediaItem}" >
          <media:MediaAspectWrapper.Template>
            <ControlTemplate>
              <Grid DataContext="{Binding RelativeSource={RelativeSource TemplatedParent}}">
                <Image HorizontalAlignment="Right" VerticalAlignment="Center" Stretch="Fill" Width="37" Height="24" Margin="-5,0,15,0" >
                  <Image.Source>
                    <MultiBinding Converter="{StaticResource ExpressionMultiValueConverter}" ConverterParameter="{}{0} ? {1} : {2}">
                      <Binding Path="PlayCount"/>
                      <Binding Source=""/>
                      <Binding Source="unwatched_icon.png"/>
                    </MultiBinding>
                  </Image.Source>
                </Image>
              </Grid>
            </ControlTemplate>
          </media:MediaAspectWrapper.Template>
        </media:MediaAspectWrapper-->


</Grid>